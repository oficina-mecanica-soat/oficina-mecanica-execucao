variable "kubeconfig_path" {
  type        = string
  description = "Kubeconfig temporário gerado pela esteira para o EKS compartilhado."
}

variable "image_uri" {
  type        = string
  description = "Imagem imutável sha-* do registry próprio, gerenciado pela infraestrutura Kubernetes."
  validation {
    condition     = can(regex(":sha-[0-9a-f]{40}$", var.image_uri))
    error_message = "Use uma imagem com tag sha- seguida do SHA completo do commit."
  }
}

variable "jwt_secret" {
  type        = string
  sensitive   = true
  description = "Chave compatível com os emissores atuais; segue a concessão acadêmica da API."
  validation {
    condition     = length(var.jwt_secret) >= 32
    error_message = "A chave JWT deve ter pelo menos 32 caracteres."
  }
}

variable "jwt_issuer" {
  type        = string
  description = "Issuer aprovado na integração JWT."
}

variable "jwt_audience" {
  type        = string
  description = "Audience aprovada na integração JWT."
}

variable "cluster_name" {
  type        = string
  description = "Cluster compartilhado obtido do contrato SSM; registrado no state para recuperação segura de destroy."
}
