resource "kubernetes_secret_v1" "jwt" {
  metadata {
    name      = "execucao-jwt"
    namespace = kubernetes_namespace_v1.execucao.metadata[0].name
  }

  data = {
    "Jwt__Secret" = var.jwt_secret
  }
}
