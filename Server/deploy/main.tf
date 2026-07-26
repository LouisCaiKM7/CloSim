# main.tf
# ---------------------------------------------------------------------------
# CloSim master server infrastructure.
#
# Target architecture: AWS App Runner running the container image from ECR.
#   - App Runner gives managed HTTPS, autoscaling, and a public URL for a
#     single stateless HTTP service with the least moving parts.
#   - The client API key is stored in Secrets Manager and injected as an env
#     var reference (value never appears in Terraform state as plaintext input).
#
# Alternative (not used here): ECS Fargate behind an Application Load Balancer.
#   That gives finer networking/VPC control and WebSocket/gRPC support, at the
#   cost of provisioning a VPC, subnets, ALB, target groups, listeners, and an
#   ACM cert. Overkill for a stateless in-memory HTTP directory API — hence
#   App Runner. Swap in that stack here if you later need VPC-private egress
#   or an ALB.
# ---------------------------------------------------------------------------

locals {
  common_tags = merge(
    {
      Project   = var.project_name
      ManagedBy = "terraform"
    },
    var.tags,
  )
}

# ---------------------------------------------------------------------------
# ECR repository (optional — create here or reference an existing one)
# ---------------------------------------------------------------------------

resource "aws_ecr_repository" "this" {
  count = var.create_ecr_repo ? 1 : 0

  name                 = var.project_name
  image_tag_mutability = "MUTABLE"
  force_delete         = true

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = local.common_tags
}

data "aws_ecr_repository" "existing" {
  count = var.create_ecr_repo ? 0 : 1
  name  = var.project_name
}

locals {
  ecr_repository_url = var.create_ecr_repo ? aws_ecr_repository.this[0].repository_url : data.aws_ecr_repository.existing[0].repository_url
}

# ---------------------------------------------------------------------------
# Secrets Manager — CLIENT_API_KEYS
#   The secret VALUE is a blank placeholder. `ignore_changes` on the value lets
#   the user set the real key(s) out-of-band (console / aws CLI) without
#   Terraform reverting them on the next apply.
# ---------------------------------------------------------------------------

resource "aws_secretsmanager_secret" "client_api_keys" {
  name        = "${var.project_name}/client-api-keys"
  description = "Comma-separated client API keys accepted by the CloSim master server (X-Api-Key)."
  tags        = local.common_tags
}

resource "aws_secretsmanager_secret_version" "client_api_keys" {
  secret_id = aws_secretsmanager_secret.client_api_keys.id
  # Blank placeholder. Set the real value out-of-band, e.g.:
  #   aws secretsmanager put-secret-value \
  #     --secret-id <arn> --secret-string "key1,key2"
  # TODO: user provides the real key(s) out-of-band.
  secret_string = var.client_api_keys != "" ? var.client_api_keys : "PLACEHOLDER_SET_OUT_OF_BAND"

  lifecycle {
    ignore_changes = [secret_string]
  }
}

# ---------------------------------------------------------------------------
# IAM — App Runner access role (ECR pull)
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "apprunner_build_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["build.apprunner.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "apprunner_access" {
  name               = "${var.project_name}-apprunner-access"
  assume_role_policy = data.aws_iam_policy_document.apprunner_build_assume.json
  tags               = local.common_tags
}

resource "aws_iam_role_policy_attachment" "apprunner_ecr_access" {
  role       = aws_iam_role.apprunner_access.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSAppRunnerServicePolicyForECRAccess"
}

# ---------------------------------------------------------------------------
# IAM — App Runner instance role (read the secret at runtime)
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "apprunner_tasks_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["tasks.apprunner.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "apprunner_instance" {
  name               = "${var.project_name}-apprunner-instance"
  assume_role_policy = data.aws_iam_policy_document.apprunner_tasks_assume.json
  tags               = local.common_tags
}

data "aws_iam_policy_document" "read_secret" {
  statement {
    sid       = "ReadClientApiKeysSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.client_api_keys.arn]
  }
}

resource "aws_iam_role_policy" "apprunner_read_secret" {
  name   = "${var.project_name}-read-secret"
  role   = aws_iam_role.apprunner_instance.id
  policy = data.aws_iam_policy_document.read_secret.json
}

# ---------------------------------------------------------------------------
# Autoscaling configuration
# ---------------------------------------------------------------------------

resource "aws_apprunner_auto_scaling_configuration_version" "this" {
  auto_scaling_configuration_name = var.project_name

  max_concurrency = var.max_concurrency
  min_size        = var.min_size
  max_size        = var.max_size

  tags = local.common_tags
}

# ---------------------------------------------------------------------------
# App Runner service
# ---------------------------------------------------------------------------

resource "aws_apprunner_service" "this" {
  service_name = var.project_name

  source_configuration {
    authentication_configuration {
      access_role_arn = aws_iam_role.apprunner_access.arn
    }

    auto_deployments_enabled = false

    image_repository {
      image_identifier      = var.ecr_image_uri # TODO: user provides (see variables.tf)
      image_repository_type = "ECR"

      image_configuration {
        port = tostring(var.container_port)

        runtime_environment_variables = {
          PORT             = tostring(var.container_port)
          BIND_HOST        = var.bind_host
          ROOM_TTL_SECONDS = tostring(var.room_ttl_seconds)
          HEARTBEAT_SECONDS = tostring(var.heartbeat_seconds)
          MAX_ROOMS        = tostring(var.max_rooms)
          REGION_LABEL     = var.region_label
          LOG_LEVEL        = var.log_level

          # --- Replay persistence (see replays.tf) ---
          # Blank bucket => the service uses its in-memory dev blob store (no persistence).
          REPLAY_S3_BUCKET                = local.replay_enabled ? aws_s3_bucket.replays[0].bucket : ""
          REPLAY_S3_REGION                = var.aws_region
          REPLAY_S3_PREFIX                = var.replay_s3_prefix
          # Blank table => in-memory metadata store.
          REPLAY_TABLE                    = local.replay_table ? aws_dynamodb_table.replays[0].name : ""
          REPLAY_UPLOAD_URL_TTL_SECONDS   = tostring(var.replay_upload_url_ttl_seconds)
          REPLAY_DOWNLOAD_URL_TTL_SECONDS = tostring(var.replay_download_url_ttl_seconds)
          REPLAY_MAX_SIZE_BYTES           = tostring(var.replay_max_size_bytes)
        }

        # Secret injected by reference — App Runner resolves it at runtime
        # using the instance role. The plaintext key never lives in the
        # container definition or Terraform plan output.
        runtime_environment_secrets = {
          CLIENT_API_KEYS = aws_secretsmanager_secret.client_api_keys.arn
        }
      }
    }
  }

  instance_configuration {
    cpu               = var.cpu
    memory            = var.memory
    instance_role_arn = aws_iam_role.apprunner_instance.arn
  }

  health_check_configuration {
    protocol            = "HTTP"
    path                = "/healthz"
    interval            = 10
    timeout             = 5
    healthy_threshold   = 1
    unhealthy_threshold = 5
  }

  auto_scaling_configuration_arn = aws_apprunner_auto_scaling_configuration_version.this.arn

  tags = local.common_tags

  depends_on = [
    aws_iam_role_policy_attachment.apprunner_ecr_access,
    aws_iam_role_policy.apprunner_read_secret,
    aws_iam_role_policy.apprunner_replays_access,
    aws_secretsmanager_secret_version.client_api_keys,
  ]
}

# ---------------------------------------------------------------------------
# Optional custom domain association
#   Create the DNS validation + CNAME records shown in the AWS console/API
#   after apply. Leave custom_domain blank to skip.
# ---------------------------------------------------------------------------

resource "aws_apprunner_custom_domain_association" "this" {
  count = var.custom_domain != "" ? 1 : 0

  domain_name          = var.custom_domain # TODO: user provides (optional)
  service_arn          = aws_apprunner_service.this.arn
  enable_www_subdomain = false
}
