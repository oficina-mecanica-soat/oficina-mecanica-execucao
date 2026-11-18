resource "aws_dynamodb_table" "execucao" {
  name         = local.execucao_resource_prefix
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "PK"
  range_key    = "SK"

  attribute {
    name = "PK"
    type = "S"
  }

  attribute {
    name = "SK"
    type = "S"
  }

  tags = local.execucao_common_tags
}

resource "aws_sqs_queue" "dlq" {
  name                        = "${local.execucao_resource_prefix}-dlq.fifo"
  fifo_queue                  = true
  content_based_deduplication = false
  message_retention_seconds   = 1209600
  sqs_managed_sse_enabled     = true
  tags                        = local.execucao_common_tags
}

resource "aws_sqs_queue" "entrada" {
  name                        = "${local.execucao_resource_prefix}.fifo"
  fifo_queue                  = true
  content_based_deduplication = false
  visibility_timeout_seconds  = 60
  receive_wait_time_seconds   = 20
  message_retention_seconds   = 345600
  sqs_managed_sse_enabled     = true
  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq.arn
    maxReceiveCount     = 5
  })
  tags = local.execucao_common_tags
}

resource "aws_sqs_queue_redrive_allow_policy" "dlq" {
  queue_url = aws_sqs_queue.dlq.url
  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = [aws_sqs_queue.entrada.arn]
  })
}

resource "aws_ssm_parameter" "execucao" {
  for_each    = local.execucao_contracts
  name        = "${local.execucao_ssm_prefix}/${each.key}"
  description = "Contrato não sensível do serviço de Execução: ${each.key}."
  type        = "String"
  value       = each.value
  tags        = local.execucao_common_tags
}

resource "aws_ssm_parameter" "execucao_status" {
  name        = "/oficina-mecanica/${var.environment}/status/execucao"
  description = "Disponibilidade da infraestrutura de dados/mensageria; não comprova prontidão do workload."
  type        = "String"
  value       = "ready"
  depends_on  = [aws_ssm_parameter.execucao, aws_sqs_queue_redrive_allow_policy.dlq]
  tags        = local.execucao_common_tags
}
