locals {
  labels  = { app = "oficina-mecanica-execucao" }
  version = regex("sha-[0-9a-f]{40}$", var.image_uri)
  datadog_labels = {
    "tags.datadoghq.com/env"     = var.environment
    "tags.datadoghq.com/service" = "oficina-mecanica-execucao"
    "tags.datadoghq.com/version" = local.version
  }
  processamento_env = merge({
    ProcessamentoRemoto__Habilitado = tostring(var.processamento_remoto_habilitado)
    ProcessamentoLocal__Habilitado  = "false"
    }, var.processamento_remoto_habilitado ? {
    ProcessamentoRemoto__ContaEsperada       = data.aws_caller_identity.current.account_id
    ProcessamentoRemoto__Regiao              = split(":", aws_sqs_queue.entrada.arn)[3]
    ProcessamentoRemoto__Tabela              = aws_dynamodb_table.execucao.name
    ProcessamentoRemoto__FilaEntradaUrl      = aws_sqs_queue.entrada.url
    ProcessamentoRemoto__FilaResultadosOsUrl = var.os_entrada_queue_url
  } : {})
}

resource "kubernetes_namespace_v1" "execucao" {
  metadata {
    name = "oficina-mecanica-execucao"
  }
}

resource "kubernetes_secret_v1" "jwt" {
  metadata {
    name      = "execucao-jwt"
    namespace = kubernetes_namespace_v1.execucao.metadata[0].name
  }
  data = { "Jwt__Secret" = var.jwt_secret }
}

resource "kubernetes_deployment_v1" "execucao" {
  depends_on = [aws_ssm_parameter.execucao_status]

  metadata {
    name      = "oficina-mecanica-execucao"
    namespace = kubernetes_namespace_v1.execucao.metadata[0].name
  }
  spec {
    replicas = 1
    selector {
      match_labels = local.labels
    }
    template {
      metadata {
        labels = merge(local.labels, local.datadog_labels, { "admission.datadoghq.com/enabled" = "true" })
        annotations = {
          "ad.datadoghq.com/execucao.logs"             = jsonencode([{ source = "csharp", service = "oficina-mecanica-execucao" }])
          "admission.datadoghq.com/dotnet-lib.version" = "3.50.0"
        }
      }
      spec {
        container {
          name  = "execucao"
          image = var.image_uri
          port {
            container_port = 8080
          }
          env {
            name  = "ASPNETCORE_URLS"
            value = "http://+:8080"
          }
          dynamic "env" {
            for_each = local.processamento_env
            content {
              name  = env.key
              value = env.value
            }
          }
          env {
            name  = "DD_ENV"
            value = var.environment
          }
          env {
            name  = "DD_SERVICE"
            value = "oficina-mecanica-execucao"
          }
          env {
            name  = "DD_VERSION"
            value = local.version
          }
          env {
            name  = "DD_LOGS_INJECTION"
            value = "true"
          }
          env {
            name  = "DD_TRACE_PROPAGATION_STYLE_INJECT"
            value = "tracecontext"
          }
          env {
            name  = "DD_TRACE_PROPAGATION_STYLE_EXTRACT"
            value = "tracecontext"
          }
          env {
            name  = "DD_HTTP_SERVER_TAG_QUERY_STRING"
            value = "false"
          }
          env {
            name  = "Jwt__Issuer"
            value = var.jwt_issuer
          }
          env {
            name  = "Jwt__Audience"
            value = var.jwt_audience
          }
          env_from {
            secret_ref {
              name = kubernetes_secret_v1.jwt.metadata[0].name
            }
          }
          resources {
            requests = { cpu = "100m", memory = "128Mi" }
            limits   = { cpu = "500m", memory = "256Mi" }
          }
          startup_probe {
            http_get {
              path = "/api/health"
              port = 8080
            }
            failure_threshold = 30
            period_seconds    = 10
          }
          liveness_probe {
            http_get {
              path = "/api/health"
              port = 8080
            }
            period_seconds = 15
          }
          readiness_probe {
            http_get {
              path = "/api/health"
              port = 8080
            }
            period_seconds = 10
          }
        }
      }
    }
  }
  timeouts {
    create = "10m"
    update = "10m"
  }
  lifecycle {
    precondition {
      condition = (!var.processamento_remoto_habilitado || var.os_entrada_queue_url ==
      "https://sqs.${split(":", aws_sqs_queue.entrada.arn)[3]}.amazonaws.com/${data.aws_caller_identity.current.account_id}/oficina-mecanica-api-${var.environment}.fifo")
      error_message = "O modo remoto exige a entrada FIFO de OS do ambiente, conta e região do workload."
    }
  }
}

resource "kubernetes_service_v1" "execucao" {
  metadata {
    name      = "oficina-mecanica-execucao"
    namespace = kubernetes_namespace_v1.execucao.metadata[0].name
  }
  spec {
    selector = local.labels
    port {
      port        = 80
      target_port = 8080
    }
    type = "ClusterIP"
  }
}
