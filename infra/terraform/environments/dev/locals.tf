locals {
  labels = {
    app = "oficina-mecanica-execucao"
  }

  version = regex("sha-[0-9a-f]{40}$", var.image_uri)
  datadog_labels = {
    "tags.datadoghq.com/env"     = var.environment
    "tags.datadoghq.com/service" = "oficina-mecanica-execucao"
    "tags.datadoghq.com/version" = local.version
  }

  processamento_env = merge(
    {
      ProcessamentoRemoto__Habilitado = tostring(var.processamento_remoto_habilitado)
      ProcessamentoLocal__Habilitado  = "false"
    },
    var.processamento_remoto_habilitado ? {
      ProcessamentoRemoto__ContaEsperada       = data.aws_caller_identity.current.account_id
      ProcessamentoRemoto__Regiao              = split(":", aws_sqs_queue.entrada.arn)[3]
      ProcessamentoRemoto__Tabela              = aws_dynamodb_table.execucao.name
      ProcessamentoRemoto__FilaEntradaUrl      = aws_sqs_queue.entrada.url
      ProcessamentoRemoto__FilaResultadosOsUrl = var.os_entrada_queue_url
    } : {}
  )

  execucao_resource_prefix = "oficina-mecanica-execucao-${var.environment}"
  execucao_ssm_prefix      = "/oficina-mecanica/${var.environment}/execucao"
  execucao_common_tags = {
    Project     = "OficinaMecanica"
    Environment = var.environment
    ManagedBy   = "Terraform"
    Repository  = "oficina-mecanica-execucao"
  }

  execucao_contracts = {
    dynamodb_table_name = aws_dynamodb_table.execucao.name
    dynamodb_table_arn  = aws_dynamodb_table.execucao.arn
    entrada_queue_url   = aws_sqs_queue.entrada.url
    entrada_queue_arn   = aws_sqs_queue.entrada.arn
    dlq_url             = aws_sqs_queue.dlq.url
    dlq_arn             = aws_sqs_queue.dlq.arn
  }
}
