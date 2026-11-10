const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');

function destroyConfig(state, region, confirmClusterAbsent) {
  const resources = state.resources || [];
  if (resources.some(r => /module\..*provider\[/.test(r.provider || ''))) {
    throw new Error('Provider definido dentro de módulo exige tratamento específico; destroy bloqueado.');
  }
  const kubernetes = resources.filter(r => r.mode === 'managed' && r.type.startsWith('kubernetes_'));
  let config = `provider "aws" { region = ${JSON.stringify(region)} }\n`;
  if (kubernetes.length) {
    const clusters = resources.filter(r => r.mode === 'data' && r.type === 'aws_eks_cluster')
      .flatMap(r => (r.instances || []).map(i => i.attributes?.name)).filter(Boolean);
    if (!clusters.length) throw new Error('State Kubernetes sem nome comprovável de cluster; recuperação bloqueada.');
    if ([...new Set(clusters)].some(cluster => confirmClusterAbsent(cluster) === false)) return null;
    config += 'provider "kubernetes" {\n host = "https://127.0.0.1"\n token = "destroy-only"\n}\n';
    for (const r of kubernetes) {
      if (r.module || !/^kubernetes_\w+$/.test(r.type) || !/^\w+$/.test(r.name)) {
        throw new Error('Endereço Kubernetes não suportado; recuperação bloqueada.');
      }
      config += `removed {\n from = ${r.type}.${r.name}\n lifecycle { destroy = false }\n}\n`;
    }
  }
  return config;
}

function verifyPlan(plan) {
  for (const change of plan.resource_changes || []) {
    if ((change.change?.actions || []).some(a => !['no-op', 'delete', 'forget', 'read'].includes(a))) {
      throw new Error(`Plano de destroy contém ação proibida: ${change.address}`);
    }
  }
}

function main() {
  if (process.env.TERRAFORM_ACTION !== 'destroy') throw new Error('Este procedimento aceita somente destroy.');
  const dir = process.env.TF_WORKING_DIRECTORY;
  if (!dir) throw new Error('Diretório Terraform ausente.');
  const run = (cmd, args) => cp.execFileSync(cmd, args, {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']});
  if (process.argv[2] === 'verify-plan') {
    verifyPlan(JSON.parse(run('terraform', [`-chdir=${dir}`, 'show', '-json', 'tfplan'])));
    console.log('Plano validado: nenhuma criação ou atualização permitida.');
    return;
  }
  if (process.argv[2] === 'verify-state') {
    const state = JSON.parse(fs.readFileSync(path.join(dir, 'terraform.tfstate'), 'utf8'));
    if ((state.resources || []).some(r => r.mode === 'managed' && r.instances?.length)) {
      throw new Error('State ainda contém recursos gerenciados; reconciliação incompleta.');
    }
    console.log('State final sem recursos gerenciados; cache pode ser persistido.');
    return;
  }
  const identity = JSON.parse(run('aws', ['sts', 'get-caller-identity', '--output', 'json']));
  console.log(`Destroy na conta AWS ${identity.Account}.`);
  const stateFile = path.join(dir, 'terraform.tfstate');
  const state = fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, 'utf8')) : {resources: []};
  const config = destroyConfig(state, process.env.AWS_REGION || 'us-east-1', cluster => {
    try {
      run('aws', ['eks', 'describe-cluster', '--name', cluster, '--output', 'json']);
    } catch (error) {
      if (!String(error.stderr).includes('(ResourceNotFoundException)')) {
        throw new Error('Consulta EKS falhou sem confirmar ausência; recuperação bloqueada.');
      }
      console.log('EKS comprovadamente ausente; remoção declarativa dos vínculos Kubernetes no plano.');
      return;
    }
    return false;
  });
  if (config === null) {
    console.log('EKS existente: preservada a configuração normal para destruir o workload.');
    return;
  }
  const backup = fs.mkdtempSync(path.join(process.env.RUNNER_TEMP, 'destroy-config-'));
  for (const name of fs.readdirSync(dir)) {
    if ((name.endsWith('.tf') || name.endsWith('.tf.json')) && name !== 'versions.tf') {
      fs.renameSync(path.join(dir, name), path.join(backup, name));
    }
  }
  fs.writeFileSync(path.join(dir, 'destroy_only.tf'), config);
  console.log('Configuração temporária exclusiva de destroy; state preservado integralmente.');
}

module.exports = {destroyConfig, verifyPlan};
if (require.main === module) {
  try { main(); } catch (error) { console.error(error.message); process.exit(1); }
}
