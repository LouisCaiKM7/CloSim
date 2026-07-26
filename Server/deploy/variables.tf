# variables.tf
# ---------------------------------------------------------------------------
# All inputs to the stack. Real values are BLANK placeholders — fill them in
# terraform.tfvars (copy from terraform.tfvars.example) before `apply`.
# ---------------------------------------------------------------------------

variable "aws_region" {
  description = "AWS region to deploy into, e.g. us-east-1. # TODO: user provides"
  type        = string
  default     = "" # TODO: user provides
}

variable "project_name" {
  description = "Name prefix for all resources (ECR repo, App Runner service, secret, IAM roles)."
  type        = string
  default     = "closim-master"
}

# --- Container image -------------------------------------------------------

variable "create_ecr_repo" {
  description = "If true, this stack creates the ECR repository. If false, it references an existing repo by name (project_name)."
  type        = bool
  default     = true
}

variable "ecr_image_uri" {
  description = <<-EOT
    Full ECR image URI (including tag) that App Runner pulls, e.g.
    123456789012.dkr.ecr.us-east-1.amazonaws.com/closim-master:latest
    Must live in the same account/region as this stack.
    # TODO: user provides (after building + pushing the image)
  EOT
  type        = string
  default     = "" # TODO: user provides
}

# --- Service runtime config (injected as env vars) -------------------------

variable "client_api_keys" {
  description = <<-EOT
    Comma-separated list of accepted client API keys (X-Api-Key header).
    SECRET — leave blank here; the value is stored in Secrets Manager and
    should be set out-of-band (see README). # TODO: user provides
  EOT
  type        = string
  default     = "" # TODO: user provides (set in Secrets Manager, not in tfvars ideally)
  sensitive   = true
}

variable "room_ttl_seconds" {
  description = "Room time-to-live in seconds before eviction (ROOM_TTL_SECONDS)."
  type        = number
  default     = 45
}

variable "heartbeat_seconds" {
  description = "Expected client heartbeat interval in seconds (HEARTBEAT_SECONDS)."
  type        = number
  default     = 15
}

variable "max_rooms" {
  description = "Maximum number of concurrently tracked rooms (MAX_ROOMS)."
  type        = number
  default     = 5000
}

variable "region_label" {
  description = "Free-form region tag surfaced by the service (REGION_LABEL). # TODO: user provides"
  type        = string
  default     = "" # TODO: user provides
}

variable "log_level" {
  description = "Log verbosity (LOG_LEVEL): error | warn | info | debug."
  type        = string
  default     = "info"
}

variable "container_port" {
  description = "Port the container listens on (PORT). App Runner routes HTTPS to this."
  type        = number
  default     = 8080
}

variable "bind_host" {
  description = "Interface the service binds to inside the container (BIND_HOST)."
  type        = string
  default     = "0.0.0.0"
}

# --- App Runner sizing + scaling -------------------------------------------

variable "cpu" {
  description = "App Runner vCPU units, e.g. \"0.25 vCPU\", \"0.5 vCPU\", \"1 vCPU\"."
  type        = string
  default     = "0.25 vCPU"
}

variable "memory" {
  description = "App Runner memory, e.g. \"0.5 GB\", \"1 GB\", \"2 GB\"."
  type        = string
  default     = "0.5 GB"
}

variable "min_size" {
  description = "Autoscaling: minimum number of provisioned instances."
  type        = number
  default     = 1
}

variable "max_size" {
  description = "Autoscaling: maximum number of instances."
  type        = number
  default     = 5
}

variable "max_concurrency" {
  description = "Autoscaling: max concurrent requests per instance before scaling out."
  type        = number
  default     = 100
}

# --- Optional custom domain ------------------------------------------------

variable "custom_domain" {
  description = <<-EOT
    Optional custom domain to associate with the App Runner service, e.g.
    master.example.com. Leave blank to use only the generated App Runner
    default domain. DNS records must be created separately (outputs surface
    the validation records via the AWS console/API). # TODO: user provides (optional)
  EOT
  type        = string
  default     = "" # TODO: user provides (optional)
}

variable "tags" {
  description = "Extra resource tags merged into every resource."
  type        = map(string)
  default     = {}
}

# --- Replay persistence (S3 blobs + optional DynamoDB metadata) -------------
# See replays.tf. All names default from project_name; override if you need
# specific names. Set enable_replays = false to run without replay storage.

variable "enable_replays" {
  description = "Create the replay S3 bucket + grant the service access. false => replays disabled (service uses its in-memory dev stores; REPLAY_S3_BUCKET injected blank)."
  type        = bool
  default     = true
}

variable "create_replay_table" {
  description = "Create the DynamoDB metadata table (and grant access). false => the service uses its in-memory metadata store. Only meaningful when enable_replays = true."
  type        = bool
  default     = true
}

variable "replay_bucket_name" {
  description = <<-EOT
    Global-unique S3 bucket name for replay blobs. Blank => derived as
    "<project_name>-replays-<account_id>". # TODO: user provides (optional)
  EOT
  type        = string
  default     = "" # TODO: user provides (optional; blank => auto-derived)
}

variable "replay_table_name" {
  description = "DynamoDB table name for replay metadata. Blank => \"<project_name>-replays\"."
  type        = string
  default     = "" # TODO: user provides (optional; blank => auto-derived)
}

variable "replay_s3_prefix" {
  description = "Key prefix under which replay blobs are stored (REPLAY_S3_PREFIX). Must match the app default."
  type        = string
  default     = "replays/"
}

variable "replay_expiry_days" {
  description = "Auto-delete replay blobs after N days via an S3 lifecycle rule. 0 => keep forever (no lifecycle rule)."
  type        = number
  default     = 0
}

variable "replay_cors_allowed_origins" {
  description = "CORS allowed origins for direct presigned PUT/GET from the client. Presigned URLs are the real gate; \"*\" is acceptable."
  type        = list(string)
  default     = ["*"]
}

variable "replay_table_pitr" {
  description = "Enable DynamoDB point-in-time recovery on the metadata table."
  type        = bool
  default     = false
}

variable "replay_upload_url_ttl_seconds" {
  description = "TTL (seconds) of presigned upload (PUT) URLs (REPLAY_UPLOAD_URL_TTL_SECONDS)."
  type        = number
  default     = 900
}

variable "replay_download_url_ttl_seconds" {
  description = "TTL (seconds) of presigned download (GET) URLs (REPLAY_DOWNLOAD_URL_TTL_SECONDS)."
  type        = number
  default     = 900
}

variable "replay_max_size_bytes" {
  description = "Hard cap on a declared replay blob size in bytes (REPLAY_MAX_SIZE_BYTES). Default 50 MiB."
  type        = number
  default     = 52428800
}
