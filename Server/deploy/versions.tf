# versions.tf
# ---------------------------------------------------------------------------
# Why Terraform (not CDK/CloudFormation/Pulumi):
#   - Declarative, provider-agnostic HCL that is easy to read/review in a PR.
#   - First-class AWS App Runner + Secrets Manager + ECR + IAM resources.
#   - Single `terraform apply` provisions the whole stack; state is explicit.
# ---------------------------------------------------------------------------

terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.40"
    }
  }
}

provider "aws" {
  region = var.aws_region
  # Credentials come from the environment / shared config (aws configure,
  # AWS_PROFILE, or an assumed role). Never hardcode credentials here.
}
