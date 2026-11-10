const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');

function validarRegistry(nome, url, urlReal, conta, regiao) {
  if (nome !== 'oficina-mecanica-execucao') throw new Error('Contrato SSM não identifica o registry do Execução.');
  const esperado = `${conta}.dkr.ecr.${regiao}.amazonaws.com/${nome}`;
  if (url !== esperado || urlReal !== url) throw new Error('Registry SSM divergente do ECR, da conta ou da região atual.');
}

function clusterDoState(state) {
  const nomes = (state.resources || []).filter(r => r.mode === 'data' && r.type === 'aws_eks_cluster')
    .flatMap(r => (r.instances || []).map(i => i.attributes?.name)).filter(Boolean);
  const unicos = [...new Set(nomes)];
  if (unicos.length > 1) throw new Error('State com mais de um cluster; recuperação bloqueada.');
  return unicos[0] || '';
}

function main({ env = process.env, consultarAws } = {}) {
  const dir = env.TF_WORKING_DIRECTORY;
  const aws = consultarAws || ((...args) => cp.execFileSync('aws', [...args, '--region', env.AWS_REGION, '--output', 'text'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim());
  let cluster = '';
  let imagem = `destroy-only:sha-${'0'.repeat(40)}`;
  let jwt = { jwt_secret: 'destroy-only-placeholder-000000000000', jwt_issuer: 'destroy-only', jwt_audience: 'destroy-only' };
  if (env.TERRAFORM_ACTION === 'apply') {
    if (!env.JWT_SECRET || Buffer.byteLength(env.JWT_SECRET) < 32 || !env.JWT_ISSUER?.trim() || !env.JWT_AUDIENCE?.trim())
      throw new Error('Configure os secrets/variables JWT do Environment antes do deploy.');
    const prefix = `/oficina-mecanica/${env.SSM_ENVIRONMENT_NAME}`;
    const parameter = suffix => aws('ssm', 'get-parameter', '--name', `${prefix}/${suffix}`, '--query', 'Parameter.Value');
    if (parameter('status/kubernetes') !== 'ready') throw new Error('Infraestrutura Kubernetes ainda não está ready.');
    cluster = parameter('kubernetes/cluster_name');
    if (!cluster || cluster === 'None') throw new Error('Contrato SSM de cluster ausente.');
    const nome = parameter('kubernetes/execucao_ecr_repository_name');
    const url = parameter('kubernetes/execucao_ecr_repository_url');
    const real = aws('ecr', 'describe-repositories', '--repository-names', nome, '--query', 'repositories[0].repositoryUri');
    validarRegistry(nome, url, real, env.AWS_ACCOUNT_ID, env.AWS_REGION);
    imagem = `${url}:sha-${env.GITHUB_SHA}`;
    jwt = { jwt_secret: env.JWT_SECRET, jwt_issuer: env.JWT_ISSUER, jwt_audience: env.JWT_AUDIENCE };
  } else if (env.TERRAFORM_ACTION === 'destroy') {
    const arquivo = path.join(dir, 'terraform.tfstate');
    if (fs.existsSync(arquivo)) cluster = clusterDoState(JSON.parse(fs.readFileSync(arquivo, 'utf8')));
  } else throw new Error('Ação Terraform inválida.');
  const vars = { ...jwt, image_uri: imagem, kubeconfig_path: env.TF_VAR_kubeconfig_path, cluster_name: cluster };
  fs.writeFileSync(path.join(dir, 'zz_ci.auto.tfvars.json'), JSON.stringify(vars), { mode: 0o600 });
  fs.appendFileSync(env.GITHUB_OUTPUT, `ecr_image_uri=${imagem}\neks_cluster_name=${cluster}\n`);
}
module.exports = { validarRegistry, clusterDoState, main };
if (require.main === module) {
  try { main(); } catch { console.error('Preparação do ambiente falhou; contratos, permissões e state devem ser conferidos. Nenhum valor sensível foi registrado.'); process.exit(1); }
}
