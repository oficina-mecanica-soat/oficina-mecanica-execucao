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
