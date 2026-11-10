const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validarRegistry, clusterDoState } = require('./preparar-ambiente-execucao.cjs');
const { validarOwnership } = require('./validar-ownership-plano.cjs');

test('contrato aceita somente o registry de Execução da conta/região atual', () => {
  const url = '123456789012.dkr.ecr.us-east-1.amazonaws.com/oficina-mecanica-execucao';
  validarRegistry('oficina-mecanica-execucao', url, url, '123456789012', 'us-east-1');
  for (const [nome, ssm, real, conta, regiao] of [
    ['oficina-mecanica-api', url, url, '123456789012', 'us-east-1'],
    ['oficina-mecanica-execucao', url, url, '999999999999', 'us-east-1'],
    ['oficina-mecanica-execucao', url, url, '123456789012', 'us-west-2'],
    ['oficina-mecanica-execucao', url, 'None', '123456789012', 'us-east-1'],
    ['oficina-mecanica-execucao', 'None', url, '123456789012', 'us-east-1']
  ]) assert.throws(() => validarRegistry(nome, ssm, real, conta, regiao));
});
test('destroy encontra cluster no state, sem depender de contratos SSM vivos', () => {
  const recurso = nome => ({ mode: 'data', type: 'aws_eks_cluster', instances: [{ attributes: { name: nome } }] });
  assert.equal(clusterDoState({ resources: [recurso('cluster-existente')] }), 'cluster-existente');
  assert.equal(clusterDoState({}), '');
  assert.throws(() => clusterDoState({ resources: [recurso('um'), recurso('outro')] }));
});
test('plano do workload rejeita alterar infraestrutura compartilhada, inclusive por módulo', () => {
  const change = (type, address, mode = 'managed') => ({ type, address, mode, change: { actions: ['create'] } });
  validarOwnership({ resource_changes: [change('kubernetes_deployment_v1', 'kubernetes_deployment_v1.execucao'), change('aws_eks_cluster', 'data.aws_eks_cluster.shared', 'data')] });
  for (const recurso of [change('aws_ecr_repository', 'aws_ecr_repository.indevido'), change('aws_eks_cluster', 'aws_eks_cluster.indevido'), change('aws_db_instance', 'aws_db_instance.indevido'), change('outro_tipo', 'module.vpc.indevido')])
    assert.throws(() => validarOwnership({ resource_changes: [recurso] }));
});

test('apply usa os contratos aprovados e não exporta o segredo junto aos outputs', () => {
  const { main } = require('./preparar-ambiente-execucao.cjs');
  const fs = require('node:fs');
  const os = require('node:os');
  const path = require('node:path');
  const prefix = path.join(os.tmpdir(), 'execucao-contrato-');
  const dir = fs.mkdtempSync(prefix);
  const url = '123456789012.dkr.ecr.us-east-1.amazonaws.com/oficina-mecanica-execucao';
  const env = { TF_WORKING_DIRECTORY: dir, GITHUB_OUTPUT: path.join(dir, 'outputs'), TERRAFORM_ACTION: 'apply', JWT_SECRET: 'a'.repeat(32), JWT_ISSUER: 'emissor', JWT_AUDIENCE: 'audiencia', AWS_REGION: 'us-east-1', AWS_ACCOUNT_ID: '123456789012', SSM_ENVIRONMENT_NAME: 'development', GITHUB_SHA: 'b'.repeat(40), TF_VAR_kubeconfig_path: 'kubeconfig-local' };
  const pedidos = [];
  try {
    main({ env, consultarAws: (...args) => {
      pedidos.push(args);
      if (args[0] === 'ecr') return url;
      const parametro = args[args.indexOf('--name') + 1];
      const contratos = {
        '/oficina-mecanica/development/status/kubernetes': 'ready',
        '/oficina-mecanica/development/kubernetes/cluster_name': 'cluster-documentado',
        '/oficina-mecanica/development/kubernetes/execucao_ecr_repository_name': 'oficina-mecanica-execucao',
        '/oficina-mecanica/development/kubernetes/execucao_ecr_repository_url': url
      };
      assert.ok(parametro in contratos);
      return contratos[parametro];
    } });
    const vars = JSON.parse(fs.readFileSync(path.join(dir, 'zz_ci.auto.tfvars.json'), 'utf8'));
    assert.equal(vars.cluster_name, 'cluster-documentado');
    assert.equal(vars.image_uri, `${url}:sha-${env.GITHUB_SHA}`);
    assert.equal(vars.jwt_secret, env.JWT_SECRET);
    assert.equal(pedidos.length, 5);
    assert.ok(!fs.readFileSync(env.GITHUB_OUTPUT, 'utf8').includes(env.JWT_SECRET));
    assert.throws(() => main({ env, consultarAws: () => { throw new Error('AccessDenied'); } }));
  } finally {
    assert.ok(path.resolve(dir).startsWith(path.resolve(prefix)));
    fs.rmSync(dir, { recursive: true });
  }
});

test('destroy prepara apenas os dados locais sem consultar SSM/ECR nem exigir JWT', () => {
  const { main } = require('./preparar-ambiente-execucao.cjs');
  const fs = require('node:fs');
  const os = require('node:os');
  const path = require('node:path');
  const prefix = path.join(os.tmpdir(), 'execucao-contrato-');
  const dir = fs.mkdtempSync(prefix);
  try {
    fs.writeFileSync(path.join(dir, 'terraform.tfstate'), JSON.stringify({ resources: [{ mode: 'data', type: 'aws_eks_cluster', instances: [{ attributes: { name: 'cluster-no-state' } }] }] }));
    const env = { TF_WORKING_DIRECTORY: dir, GITHUB_OUTPUT: path.join(dir, 'outputs'), TERRAFORM_ACTION: 'destroy', TF_VAR_kubeconfig_path: 'local' };
    main({ env, consultarAws: () => { throw new Error('Destroy não deve consultar contratos de plataforma'); } });
    const vars = JSON.parse(fs.readFileSync(path.join(dir, 'zz_ci.auto.tfvars.json'), 'utf8'));
    assert.equal(vars.cluster_name, 'cluster-no-state');
    assert.match(vars.image_uri, /^destroy-only:sha-0{40}$/);
  } finally {
    assert.ok(path.resolve(dir).startsWith(path.resolve(prefix)));
    fs.rmSync(dir, { recursive: true });
  }
});
