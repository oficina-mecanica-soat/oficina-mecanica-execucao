mock_provider "aws" {
  override_during = plan
  mock_data "aws_caller_identity" {
    defaults = { account_id = "123456789012" }
  }
  mock_resource "aws_dynamodb_table" {
    defaults = {
      arn = "arn:aws:dynamodb:us-east-1:123456789012:table/oficina-mecanica-execucao-development"
    }
  }
}
mock_provider "kubernetes" {}

variables {
  environment     = "development"
  cluster_name    = "cluster-local-simulado"
  kubeconfig_path = "nao-utilizado-pelo-provider-simulado"
  image_uri       = "local:sha-0000000000000000000000000000000000000000"
  jwt_secret      = "somente-teste-local-sem-credencial-real"
  jwt_issuer      = "teste"
  jwt_audience    = "teste"
}

override_resource {
  override_during = plan
  target          = aws_sqs_queue.dlq
  values = {
    arn = "arn:aws:sqs:us-east-1:123456789012:oficina-mecanica-execucao-development-dlq.fifo"
    url = "https://sqs.us-east-1.amazonaws.com/123456789012/oficina-mecanica-execucao-development-dlq.fifo"
  }
}
override_resource {
  override_during = plan
  target          = aws_sqs_queue.entrada
  values = {
    arn = "arn:aws:sqs:us-east-1:123456789012:oficina-mecanica-execucao-development.fifo"
    url = "https://sqs.us-east-1.amazonaws.com/123456789012/oficina-mecanica-execucao-development.fifo"
  }
}

run "contrato_dynamodb_compativel_com_repositorio" {
  command = plan
  assert {
    condition = (
      aws_dynamodb_table.execucao.billing_mode == "PAY_PER_REQUEST"
      && aws_dynamodb_table.execucao.hash_key == "PK"
      && aws_dynamodb_table.execucao.range_key == "SK"
      && alltrue([for atributo in aws_dynamodb_table.execucao.attribute : atributo.type == "S"])
    )
    error_message = "O armazenamento transacional exige PK/SK string e capacidade sob demanda."
  }
}

run "parametros_fifo_e_compensacao_conforme_rfc" {
  command = plan
  assert {
    condition = (
      aws_sqs_queue.entrada.fifo_queue && aws_sqs_queue.dlq.fifo_queue
      && !aws_sqs_queue.entrada.content_based_deduplication
      && aws_sqs_queue.entrada.visibility_timeout_seconds == 60
      && aws_sqs_queue.entrada.receive_wait_time_seconds == 20
      && aws_sqs_queue.entrada.message_retention_seconds == 345600
      && aws_sqs_queue.dlq.message_retention_seconds == 1209600
      && jsondecode(aws_sqs_queue.entrada.redrive_policy).maxReceiveCount == 5
      && jsondecode(aws_sqs_queue.entrada.redrive_policy).deadLetterTargetArn == aws_sqs_queue.dlq.arn
    )
    error_message = "FIFO, redrive e retenções devem preservar a RFC-0007; mensagemId será enviado pelo produtor."
  }
  assert {
    condition = (
      jsondecode(aws_sqs_queue_redrive_allow_policy.dlq.redrive_allow_policy).redrivePermission == "byQueue"
      && jsondecode(aws_sqs_queue_redrive_allow_policy.dlq.redrive_allow_policy).sourceQueueArns == [aws_sqs_queue.entrada.arn]
    )
    error_message = "A DLQ deve aceitar redrive somente da fila de entrada do Execução."
  }
}

run "contratos_ssm_apontam_para_os_recursos_do_owner" {
  command = plan
  assert {
    condition = (
      aws_ssm_parameter.execucao["dynamodb_table_name"].value == aws_dynamodb_table.execucao.name
      && aws_ssm_parameter.execucao["dynamodb_table_arn"].value == aws_dynamodb_table.execucao.arn
      && aws_ssm_parameter.execucao["entrada_queue_url"].value == aws_sqs_queue.entrada.url
      && aws_ssm_parameter.execucao["entrada_queue_arn"].value == aws_sqs_queue.entrada.arn
      && aws_ssm_parameter.execucao["dlq_url"].value == aws_sqs_queue.dlq.url
      && aws_ssm_parameter.execucao["dlq_arn"].value == aws_sqs_queue.dlq.arn
      && alltrue([for parametro in aws_ssm_parameter.execucao : parametro.type == "String"])
    )
    error_message = "Contratos devem publicar os valores reais dos recursos próprios, sem credenciais."
  }
}

run "ambiente_parametriza_nomes_e_contratos" {
  command = plan
  variables {
    environment = "homologation"
  }
  assert {
    condition = (
      aws_dynamodb_table.execucao.name == "oficina-mecanica-execucao-homologation"
      && aws_sqs_queue.entrada.name == "oficina-mecanica-execucao-homologation.fifo"
      && aws_ssm_parameter.execucao["entrada_queue_url"].name == "/oficina-mecanica/homologation/execucao/entrada_queue_url"
      && aws_ssm_parameter.execucao_status.name == "/oficina-mecanica/homologation/status/execucao"
    )
    error_message = "O ambiente não pode cair silenciosamente em development."
  }
}

run "observabilidade_segue_referencia_api" {
  command = plan
  assert {
    condition = (
      kubernetes_deployment_v1.execucao.spec[0].template[0].metadata[0].labels["tags.datadoghq.com/service"] == "oficina-mecanica-execucao"
      && kubernetes_deployment_v1.execucao.spec[0].template[0].metadata[0].annotations["admission.datadoghq.com/dotnet-lib.version"] == "3.50.0"
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "DD_VERSION"]) == "sha-0000000000000000000000000000000000000000"
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "DD_HTTP_SERVER_TAG_QUERY_STRING"]) == "false"
    )
    error_message = "Manter serviço/versão, tracer da API e proteção de query string."
  }
}

run "runtime_remoto_permanece_desabilitado_por_padrao" {
  command = plan
  assert {
    condition = (
      one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__Habilitado"]) == "false"
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoLocal__Habilitado"]) == "false"
      && !anytrue([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : contains(["ProcessamentoRemoto__ContaEsperada", "ProcessamentoRemoto__FilaResultadosOsUrl", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN"], env.name)])
    )
    error_message = "Sem ativação explícita, não habilitar runtime ou transportar credenciais do runner."
  }
}

run "runtime_remoto_recebe_recursos_proprios_e_destino_os" {
  command = plan
  variables {
    processamento_remoto_habilitado = true
    os_entrada_queue_url            = "https://sqs.us-east-1.amazonaws.com/123456789012/oficina-mecanica-api-development.fifo"
  }
  assert {
    condition = (
      one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__Habilitado"]) == "true"
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__ContaEsperada"]) == "123456789012"
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__Regiao"]) == "us-east-1"
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__Tabela"]) == aws_dynamodb_table.execucao.name
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__FilaEntradaUrl"]) == aws_sqs_queue.entrada.url
      && one([for env in kubernetes_deployment_v1.execucao.spec[0].template[0].spec[0].container[0].env : env.value if env.name == "ProcessamentoRemoto__FilaResultadosOsUrl"]) == var.os_entrada_queue_url
    )
    error_message = "Usar valores dos recursos próprios e somente o destino aprovado de OS."
  }
}

run "runtime_remoto_bloqueia_destino_de_outra_conta" {
  command = plan
  variables {
    processamento_remoto_habilitado = true
    os_entrada_queue_url            = "https://sqs.us-east-1.amazonaws.com/999999999999/oficina-mecanica-api-development.fifo"
  }
  expect_failures = [kubernetes_deployment_v1.execucao]
}

run "runtime_remoto_bloqueia_destino_ausente" {
  command = plan
  variables {
    processamento_remoto_habilitado = true
  }
  expect_failures = [kubernetes_deployment_v1.execucao]
}
