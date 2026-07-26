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
