output "execucao_contracts" {
  description = "Contratos não sensíveis publicados no prefixo SSM do Execução."
  value       = local.execucao_contracts
}

output "execucao_ssm_prefix" {
  description = "Prefixo dos contratos de dados/mensageria do Execução."
  value       = local.execucao_ssm_prefix
}
