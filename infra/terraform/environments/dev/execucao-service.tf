resource "kubernetes_service_v1" "execucao" {
  metadata {
    name      = "oficina-mecanica-execucao"
    namespace = kubernetes_namespace_v1.execucao.metadata[0].name
  }

  spec {
    type     = "NodePort"
    selector = local.labels

    port {
      name        = "http"
      port        = 80
      target_port = 8080
      protocol    = "TCP"
      node_port   = var.node_port
    }
  }

  depends_on = [kubernetes_deployment_v1.execucao]
}
