const fs = require('node:fs');
function validarOwnership(plan) {
  const bloqueados = new Set(['aws_vpc', 'aws_subnet', 'aws_nat_gateway', 'aws_internet_gateway', 'aws_route_table', 'aws_route', 'aws_route_table_association', 'aws_eks_cluster', 'aws_eks_node_group', 'aws_db_instance', 'aws_db_subnet_group', 'aws_ecr_repository', 'aws_security_group', 'aws_security_group_rule', 'aws_vpc_security_group_ingress_rule', 'aws_vpc_security_group_egress_rule', 'aws_lb', 'aws_lb_listener', 'aws_lb_target_group']);
  const modules = ['module.vpc', 'module.eks', 'module.rds', 'module.ecr'];
  const violacoes = (plan.resource_changes || []).filter(change => change.mode === 'managed'
    && (bloqueados.has(change.type) || modules.some(prefix => change.address.startsWith(prefix)))
    && (change.change?.actions || []).some(action => ['create', 'update', 'delete', 'replace'].includes(action)));
  if (violacoes.length) throw new Error(`Infraestrutura compartilhada proibida no plano: ${violacoes.map(c => c.address).join(', ')}`);
}
module.exports = { validarOwnership };
if (require.main === module) {
  try { validarOwnership(JSON.parse(fs.readFileSync(process.argv[2], 'utf8'))); }
  catch (error) { console.error(error.message); process.exit(1); }
}
