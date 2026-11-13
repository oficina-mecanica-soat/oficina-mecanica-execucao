resource "kubernetes_namespace_v1" "execucao" {
  metadata {
    name = "oficina-mecanica-execucao"
  }
}
