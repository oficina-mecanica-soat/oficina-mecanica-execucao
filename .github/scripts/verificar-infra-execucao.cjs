const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');

function contratosDoState(state) {
  const atributos = (tipo, nome) => (state.resources || []).find(r => r.mode === 'managed' && !r.module && r.type === tipo && r.name === nome)?.instances?.[0]?.attributes || {};
  const tabela = atributos('aws_dynamodb_table', 'execucao');
  const entrada = atributos('aws_sqs_queue', 'entrada');
  const dlq = atributos('aws_sqs_queue', 'dlq');
  return {
    dynamodb_table_name: tabela.name || tabela.id || '', dynamodb_table_arn: tabela.arn || '',
    entrada_queue_url: entrada.url || entrada.id || '', entrada_queue_arn: entrada.arn || '',
    dlq_url: dlq.url || dlq.id || '', dlq_arn: dlq.arn || ''
  };
}

const atributosContagem = ['ApproximateNumberOfMessages', 'ApproximateNumberOfMessagesNotVisible', 'ApproximateNumberOfMessagesDelayed'];
function verificarPendencias(contratos, consultar) {
  if (contratos.dynamodb_table_name) {
    const outbox = consultar('dynamodb', 'query', '--table-name', contratos.dynamodb_table_name, '--key-condition-expression', 'PK = :pk', '--expression-attribute-values', JSON.stringify({ ':pk': { S: 'OUTBOX#PENDENTE' } }), '--consistent-read', '--select', 'COUNT', '--limit', '1');
    if (outbox !== null && (!Number.isInteger(outbox.Count) || outbox.Count !== 0))
      throw new Error('Outbox pendente ou resposta inválida: destroy bloqueado.');
  }
  for (const url of [contratos.entrada_queue_url, contratos.dlq_url].filter(Boolean)) {
    const resposta = consultar('sqs', 'get-queue-attributes', '--queue-url', url, '--attribute-names', ...atributosContagem);
    if (resposta === null) continue;
    for (const atributo of atributosContagem) {
      const valor = resposta.Attributes?.[atributo];
      if (typeof valor !== 'string' || !/^\d+$/.test(valor) || Number(valor) !== 0)
        throw new Error('Mensagens pendentes ou contagem inválida na fila/DLQ: destroy bloqueado.');
    }
  }
}

function verificarInfra(contratos, prefix, consultar) {
  if (Object.values(contratos).some(valor => !valor)) throw new Error('State incompleto: contratos de dados/mensageria ausentes.');
  const tabela = consultar('dynamodb', 'describe-table', '--table-name', contratos.dynamodb_table_name)?.Table;
  if (tabela?.TableStatus !== 'ACTIVE' || tabela.BillingModeSummary?.BillingMode !== 'PAY_PER_REQUEST'
    || !tabela.KeySchema?.some(k => k.AttributeName === 'PK' && k.KeyType === 'HASH')
    || !tabela.KeySchema?.some(k => k.AttributeName === 'SK' && k.KeyType === 'RANGE'))
    throw new Error('Tabela real não corresponde ao contrato DynamoDB.');
  for (const [url, arn, dlq] of [[contratos.entrada_queue_url, contratos.entrada_queue_arn, false], [contratos.dlq_url, contratos.dlq_arn, true]]) {
    const atributos = consultar('sqs', 'get-queue-attributes', '--queue-url', url, '--attribute-names', 'All')?.Attributes;
    if (!atributos || atributos.QueueArn !== arn || atributos.FifoQueue !== 'true'
      || atributos.ContentBasedDeduplication !== 'false' || atributos.MessageRetentionPeriod !== (dlq ? '1209600' : '345600'))
      throw new Error('Fila real não corresponde ao contrato FIFO/retenção.');
    if (!dlq) {
      const redrive = JSON.parse(atributos.RedrivePolicy || '{}');
      if (atributos.VisibilityTimeout !== '60' || atributos.ReceiveMessageWaitTimeSeconds !== '20'
        || Number(redrive.maxReceiveCount) !== 5 || redrive.deadLetterTargetArn !== contratos.dlq_arn)
        throw new Error('Fila real não corresponde à recuperação definida na RFC.');
    }
  }
  for (const [nome, valor] of Object.entries(contratos)) {
    const parametro = consultar('ssm', 'get-parameter', '--name', `${prefix}/execucao/${nome}`)?.Parameter;
    if (parametro?.Value !== valor || parametro.Type !== 'String') throw new Error('Contrato SSM divergente do state.');
  }
  if (consultar('ssm', 'get-parameter', '--name', `${prefix}/status/execucao`)?.Parameter?.Value !== 'ready')
    throw new Error('Status de infraestrutura do Execução não está ready.');
}

function verificarRemocao(contratos, prefix, consultar) {
  const confirmar = (...args) => { if (consultar(...args) !== null) throw new Error('Recurso ainda existe após destroy.'); };
  if (contratos.dynamodb_table_name) confirmar('dynamodb', 'describe-table', '--table-name', contratos.dynamodb_table_name);
  for (const url of [contratos.entrada_queue_url, contratos.dlq_url].filter(Boolean)) confirmar('sqs', 'get-queue-attributes', '--queue-url', url, '--attribute-names', 'QueueArn');
  for (const nome of Object.keys(contratos)) confirmar('ssm', 'get-parameter', '--name', `${prefix}/execucao/${nome}`);
  confirmar('ssm', 'get-parameter', '--name', `${prefix}/status/execucao`);
}

function consultarAws(...args) {
  try {
    return JSON.parse(cp.execFileSync('aws', [...args, '--region', process.env.AWS_REGION || 'us-east-1', '--output', 'json'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }));
  } catch (error) {
    const ausencias = { dynamodb: '(ResourceNotFoundException)', sqs: '(AWS.SimpleQueueService.NonExistentQueue)', ssm: '(ParameterNotFound)' };
    if (ausencias[args[0]] && String(error.stderr).includes(ausencias[args[0]])) return null;
    throw new Error(`Consulta ${args[0]}/${args[1]} falhou sem comprovar ausência; operação bloqueada.`);
  }
}
function main() {
  const dir = process.env.TF_WORKING_DIRECTORY;
  const contexto = path.join(dir, 'execucao-destroy-context.json');
  const stateFile = path.join(dir, 'terraform.tfstate');
  const contratos = contratosDoState(fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, 'utf8')) : {});
  const prefix = `/oficina-mecanica/${process.env.SSM_ENVIRONMENT_NAME}`;
  switch (process.argv[2]) {
    case 'before-destroy':
      verificarPendencias(contratos, consultarAws);
      fs.writeFileSync(contexto, JSON.stringify(contratos));
      console.log('Nenhuma pendência observada em filas/DLQ/outbox; produtores devem permanecer parados.');
      break;
    case 'after-destroy':
      verificarRemocao(JSON.parse(fs.readFileSync(contexto, 'utf8')), prefix, consultarAws);
      console.log('Tabela, filas e contratos de infraestrutura comprovadamente ausentes.');
      break;
    case 'after-apply':
      verificarInfra(contratos, prefix, consultarAws);
      console.log('Recursos e contratos de infraestrutura conferidos; não comprova acesso do workload.');
      break;
    default: throw new Error('Modo de verificação inválido.');
  }
}
module.exports = { contratosDoState, verificarPendencias, verificarInfra, verificarRemocao, consultarAws };
if (require.main === module) { try { main(); } catch (error) { console.error(error.message); process.exit(1); } }
