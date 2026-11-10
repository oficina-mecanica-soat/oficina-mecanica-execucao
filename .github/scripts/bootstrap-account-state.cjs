const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');

function projectResource(resource) {
  return /oficina.?mecanica/i.test(JSON.stringify(resource));
}
function checkEmpty(label, resources) {
  if (!Array.isArray(resources)) throw new Error(`Resposta inválida: ${label}`);
  if (resources.some(projectResource)) throw new Error(`${label}: há recursos do projeto; bootstrap sem state bloqueado.`);
}
function main() {
  const dir = process.env.TF_WORKING_DIRECTORY;
  const stateFile = path.join(dir, 'terraform.tfstate');
  const aws = (...args) => JSON.parse(cp.execFileSync('aws', [...args, '--region', process.env.AWS_REGION || 'us-east-1', '--output', 'json'], {encoding:'utf8',stdio:['ignore','pipe','pipe']}));
  const account = aws('sts','get-caller-identity').Account;
  if (fs.existsSync(stateFile)) {
    const state = JSON.parse(fs.readFileSync(stateFile,'utf8'));
    for (const r of (state.resources || []).filter(r=>r.mode==='managed')) {
      for (const i of r.instances || []) {
        const a = i.attributes || {};
        const accounts = [a.owner_id,a.registry_id,a.arn?.match(/^arn:[^:]+:[^:]*:[^:]*:(\d{12}):/)?.[1],i.identity?.account_id].filter(Boolean);
        if (accounts.some(id=>id!==account)) throw new Error('State pertence a outra conta AWS; operação bloqueada.');
      }
    }
    console.log(`State restaurado no escopo da conta ${account}.`);
    return;
  }
  if (process.env.TERRAFORM_ACTION !== 'destroy') throw new Error('Cache da conta ausente. Bootstrap exige destroy dedicado e ambiente vazio.');
  const checks = [
    ['EKS',['eks','list-clusters'],'clusters'],
    ['RDS',['rds','describe-db-instances'],'DBInstances'],
    ['Lambda',['lambda','list-functions'],'Functions'],
    ['API Gateway',['apigatewayv2','get-apis'],'Items'],
    ['VPC Link',['apigatewayv2','get-vpc-links'],'Items'],
    ['ECR',['ecr','describe-repositories'],'repositories'],
    ['DynamoDB',['dynamodb','list-tables'],'TableNames'],
    ['SQS',['sqs','list-queues','--max-results','1000'],'QueueUrls'],
    ['Load Balancers',['elbv2','describe-load-balancers'],'LoadBalancers'],
    ['VPC',['ec2','describe-vpcs'],'Vpcs'],
    ['Subnets',['ec2','describe-subnets'],'Subnets'],
    ['Security groups',['ec2','describe-security-groups'],'SecurityGroups'],
    ['Volumes',['ec2','describe-volumes'],'Volumes'],
    ['IPs elásticos',['ec2','describe-addresses'],'Addresses'],
    ['Secrets (somente metadados)',['secretsmanager','list-secrets','--include-planned-deletion'],'SecretList'],
    ['CloudWatch',['logs','describe-log-groups'],'logGroups'],
  ];
  for (const [label,args,key] of checks) {
    const resultado = aws(...args);
    checkEmpty(label, label === 'SQS' && !('QueueUrls' in resultado) ? [] : resultado[key]);
    console.log(`${label}: nenhum recurso do projeto encontrado.`);
  }
  const parameters = aws('ssm','describe-parameters','--parameter-filters','Key=Name,Option=BeginsWith,Values=/oficina-mecanica/development/').Parameters;
  if (!Array.isArray(parameters) || parameters.length) throw new Error('SSM: contratos existentes ou resposta inválida; bootstrap bloqueado.');
  console.log(`Bootstrap autorizado somente para destroy: conta ${account} sem recursos do projeto. Caches históricos de outras contas preservados e fora da restauração.`);
}
module.exports={checkEmpty,projectResource};
if(require.main===module) {try{main();}catch(error){console.error(error.message);process.exit(1);}}
