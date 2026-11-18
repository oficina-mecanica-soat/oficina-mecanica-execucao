data "aws_eks_cluster" "shared" {
  name = var.cluster_name
}

data "aws_caller_identity" "current" {}
