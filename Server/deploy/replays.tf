# replays.tf
# ---------------------------------------------------------------------------
# Persistent REPLAY storage for the CloSim master server.
#
# Unlike the ephemeral room directory (in-memory, no infra), replays PERSIST:
#   - Opaque replay BLOBS  -> private S3 bucket (clients PUT/GET directly via
#     short-lived presigned URLs; the API only mints those URLs).
#   - Replay METADATA      -> optional DynamoDB table (on-demand billing). When
#     not created, the service falls back to its in-memory metadata store.
#
# All of this is ADDITIVE and toggleable:
#   - enable_replays       (default true)  -> create the bucket + grant S3 access
#   - create_replay_table  (default true)  -> create the DynamoDB table + grant it
# Set enable_replays = false to run the service with replays disabled (the app
# then runs its in-memory dev stores; REPLAY_S3_BUCKET is injected blank).
#
# Every name is derived from project_name (override via the *_name vars). No
# ARNs/names are hardcoded; the App Runner instance role is granted least-priv
# access to exactly these resources.
# ---------------------------------------------------------------------------

data "aws_caller_identity" "current" {}

locals {
  replay_enabled = var.enable_replays
  replay_table   = var.enable_replays && var.create_replay_table

  # S3 bucket names are globally unique; default suffixes with the account id.
  replay_bucket_name = var.replay_bucket_name != "" ? var.replay_bucket_name : "${var.project_name}-replays-${data.aws_caller_identity.current.account_id}"
  replay_table_name  = var.replay_table_name != "" ? var.replay_table_name : "${var.project_name}-replays"
}

# ---------------------------------------------------------------------------
# S3 bucket for replay blobs (PRIVATE — never public; access is via presigned
# URLs signed by the App Runner instance role).
# ---------------------------------------------------------------------------

resource "aws_s3_bucket" "replays" {
  count = local.replay_enabled ? 1 : 0

  bucket = local.replay_bucket_name
  tags   = local.common_tags
}

# Block ALL public access — blobs are reachable only through presigned URLs.
resource "aws_s3_bucket_public_access_block" "replays" {
  count = local.replay_enabled ? 1 : 0

  bucket                  = aws_s3_bucket.replays[0].id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "replays" {
  count = local.replay_enabled ? 1 : 0

  bucket = aws_s3_bucket.replays[0].id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# CORS so the game client (a native app, but presigned PUT/GET may run through
# UnityWebRequest) can upload/download directly. Scoped to PUT/GET/HEAD.
resource "aws_s3_bucket_cors_configuration" "replays" {
  count = local.replay_enabled ? 1 : 0

  bucket = aws_s3_bucket.replays[0].id
  cors_rule {
    allowed_methods = ["PUT", "GET", "HEAD"]
    allowed_origins = var.replay_cors_allowed_origins # default ["*"] — presigned URLs are the real gate
    allowed_headers = ["*"]
    expose_headers  = ["ETag"]
    max_age_seconds = 3000
  }
}

# Optional lifecycle expiry — auto-delete blobs after N days (0 => disabled).
resource "aws_s3_bucket_lifecycle_configuration" "replays" {
  count = local.replay_enabled && var.replay_expiry_days > 0 ? 1 : 0

  bucket = aws_s3_bucket.replays[0].id
  rule {
    id     = "expire-replays"
    status = "Enabled"
    filter {
      prefix = var.replay_s3_prefix
    }
    expiration {
      days = var.replay_expiry_days
    }
    # Clean up incomplete multipart uploads (large blobs).
    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }
}

# ---------------------------------------------------------------------------
# DynamoDB table for replay metadata (optional; on-demand billing).
#   Partition key: replayId (S). GSIs let the API Query by owner / game newest
#   first (createdAtEpoch sort key) instead of scanning at scale.
# ---------------------------------------------------------------------------

resource "aws_dynamodb_table" "replays" {
  count = local.replay_table ? 1 : 0

  name         = local.replay_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "replayId"

  attribute {
    name = "replayId"
    type = "S"
  }
  attribute {
    name = "userId"
    type = "S"
  }
  attribute {
    name = "gameId"
    type = "S"
  }
  attribute {
    name = "createdAtEpoch"
    type = "N"
  }

  global_secondary_index {
    name            = "userId-index"
    hash_key        = "userId"
    range_key       = "createdAtEpoch"
    projection_type = "ALL"
  }
  global_secondary_index {
    name            = "gameId-index"
    hash_key        = "gameId"
    range_key       = "createdAtEpoch"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.replay_table_pitr
  }

  tags = local.common_tags
}

# ---------------------------------------------------------------------------
# IAM — grant the App Runner INSTANCE role least-privilege access to the
# replay bucket (object CRUD) and, when present, the metadata table.
# (aws_iam_role.apprunner_instance is defined in main.tf.)
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "replays_access" {
  count = local.replay_enabled ? 1 : 0

  statement {
    sid       = "ReplayBlobObjectAccess"
    actions   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.replays[0].arn}/${var.replay_s3_prefix}*"]
  }

  statement {
    sid       = "ReplayBucketList"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.replays[0].arn]
  }

  dynamic "statement" {
    for_each = local.replay_table ? [1] : []
    content {
      sid = "ReplayMetadataTableAccess"
      actions = [
        "dynamodb:PutItem",
        "dynamodb:GetItem",
        "dynamodb:DeleteItem",
        "dynamodb:Query",
        "dynamodb:Scan",
      ]
      resources = [
        aws_dynamodb_table.replays[0].arn,
        "${aws_dynamodb_table.replays[0].arn}/index/*",
      ]
    }
  }
}

resource "aws_iam_role_policy" "apprunner_replays_access" {
  count = local.replay_enabled ? 1 : 0

  name   = "${var.project_name}-replays-access"
  role   = aws_iam_role.apprunner_instance.id
  policy = data.aws_iam_policy_document.replays_access[0].json
}
