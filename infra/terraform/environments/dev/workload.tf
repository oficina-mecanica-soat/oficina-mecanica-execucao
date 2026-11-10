locals {
  labels = { app = "oficina-mecanica-execucao" }
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
        labels = local.labels
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
