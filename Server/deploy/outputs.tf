# outputs.tf

output "service_url" {
  description = "Public HTTPS URL of the App Runner service (its default domain). Point the game client here."
  value       = "https://${aws_apprunner_service.this.service_url}"
}

output "service_arn" {
  description = "ARN of the App Runner service."
  value       = aws_apprunner_service.this.arn
}

output "ecr_repository_url" {
  description = "ECR repository URL to tag/push the container image to."
  value       = local.ecr_repository_url
}

output "secret_arn" {
  description = "ARN of the Secrets Manager secret holding CLIENT_API_KEYS. Set its value out-of-band."
  value       = aws_secretsmanager_secret.client_api_keys.arn
}

output "custom_domain" {
  description = "Custom domain associated with the service, if any. Empty when unused. Fetch DNS validation records via the AWS console/API after apply."
  value       = var.custom_domain != "" ? var.custom_domain : ""
}

# --- Replay persistence ----------------------------------------------------

output "replay_bucket_name" {
  description = "S3 bucket holding replay blobs (empty when replays are disabled)."
  value       = local.replay_enabled ? aws_s3_bucket.replays[0].bucket : ""
}

output "replay_bucket_arn" {
  description = "ARN of the replay S3 bucket (empty when disabled)."
  value       = local.replay_enabled ? aws_s3_bucket.replays[0].arn : ""
}

output "replay_table_name" {
  description = "DynamoDB table holding replay metadata (empty when using the in-memory store)."
  value       = local.replay_table ? aws_dynamodb_table.replays[0].name : ""
}
