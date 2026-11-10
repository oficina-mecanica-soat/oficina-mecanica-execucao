const { test } = require('node:test');
const assert = require('node:assert/strict');
const { contratosDoState, verificarPendencias, verificarInfra, verificarRemocao } = require('./verificar-infra-execucao.cjs');
const contratos = { dynamodb_table_name: 'tabela', dynamodb_table_arn: 'arn:tabela', entrada_queue_url: 'entrada', entrada_queue_arn: 'arn:entrada', dlq_url: 'dlq', dlq_arn: 'arn:dlq' };
const semMensagens = { Attributes: { ApproximateNumberOfMessages: '0', ApproximateNumberOfMessagesNotVisible: '0', ApproximateNumberOfMessagesDelayed: '0' } };

test('destroy bloqueia outbox pendente e respostas inválidas, sem ler conteúdo de mensagens', () => {
  for (const Count of [1, undefined, -1]) assert.throws(() => verificarPendencias(contratos, () => ({ Count })), /Outbox/);
  verificarPendencias(contratos, (servico, operacao, ...args) => {
    if (servico === 'dynamodb') {
      assert.equal(operacao, 'query');
      assert.ok(args.includes('--consistent-read'));
      assert.ok(args.includes('COUNT'));
      assert.ok(args.some(arg => arg.includes('OUTBOX#PENDENTE')));
      return { Count: 0 };
    }
    return semMensagens;
  });
});
test('destroy bloqueia mensagens visíveis, em voo, atrasadas e em DLQ', () => {
  for (const atributo of Object.keys(semMensagens.Attributes)) {
    for (const fila of ['entrada', 'dlq']) {
      assert.throws(() => verificarPendencias(contratos, (servico, operacao, ...args) => servico === 'dynamodb' ? { Count: 0 }
        : args.includes(fila) ? { Attributes: { ...semMensagens.Attributes, [atributo]: '1' } } : semMensagens), /Mensagens pendentes/);
    }
  }
  assert.throws(() => verificarPendencias(contratos, servico => servico === 'dynamodb' ? { Count: 0 } : { Attributes: {} }), /contagem inválida/);
});
test('ausência confirmada permite recuperação; falha de acesso não vira ausência', () => {
  verificarPendencias(contratos, () => null);
  verificarRemocao(contratos, '/oficina-mecanica/development', () => null);
  assert.throws(() => verificarPendencias(contratos, () => { throw new Error('AccessDenied'); }), /AccessDenied/);
  assert.throws(() => verificarRemocao(contratos, '/oficina-mecanica/development', () => { throw new Error('AccessDenied'); }), /AccessDenied/);
  assert.throws(() => verificarRemocao(contratos, '/oficina-mecanica/development', () => ({})), /ainda existe/);
});
test('contexto de destroy vem dos recursos do state mesmo sem outputs após falha parcial', () => {
  const recurso = (type, name, attributes) => ({ mode: 'managed', type, name, instances: [{ attributes }] });
  const state = { resources: [recurso('aws_dynamodb_table', 'execucao', { name: 'tabela', arn: 'arn:tabela' }), recurso('aws_sqs_queue', 'entrada', { id: 'entrada', arn: 'arn:entrada' }), recurso('aws_sqs_queue', 'dlq', { url: 'dlq', arn: 'arn:dlq' })] };
  assert.deepEqual(contratosDoState(state), contratos);
  assert.ok(Object.values(contratosDoState({})).every(valor => valor === ''));
});
test('pós-apply confere tabela, filas, redrive e contratos reais; não presume acesso do workload', () => {
  const prefix = '/oficina-mecanica/development';
  const consultar = (servico, operacao, ...args) => {
    if (servico === 'dynamodb') return { Table: { TableStatus: 'ACTIVE', BillingModeSummary: { BillingMode: 'PAY_PER_REQUEST' }, KeySchema: [{ AttributeName: 'PK', KeyType: 'HASH' }, { AttributeName: 'SK', KeyType: 'RANGE' }] } };
    if (servico === 'sqs') return { Attributes: { QueueArn: args.includes('entrada') ? 'arn:entrada' : 'arn:dlq', FifoQueue: 'true', ContentBasedDeduplication: 'false', MessageRetentionPeriod: args.includes('entrada') ? '345600' : '1209600', VisibilityTimeout: '60', ReceiveMessageWaitTimeSeconds: '20', RedrivePolicy: JSON.stringify({ maxReceiveCount: '5', deadLetterTargetArn: 'arn:dlq' }) } };
    const nome = args[args.indexOf('--name') + 1];
    if (nome === `${prefix}/status/execucao`) return { Parameter: { Value: 'ready' } };
    const chave = nome.slice(`${prefix}/execucao/`.length);
    assert.ok(chave in contratos);
    return { Parameter: { Value: contratos[chave], Type: 'String' } };
  };
  verificarInfra(contratos, prefix, consultar);
  assert.throws(() => verificarInfra({ ...contratos, dlq_arn: '' }, prefix, consultar), /State incompleto/);
  assert.throws(() => verificarInfra(contratos, prefix, (servico, ...args) => servico === 'ssm' ? { Parameter: { Value: 'outro', Type: 'String' } } : consultar(servico, ...args)), /Contrato SSM/);
  assert.throws(() => verificarInfra(contratos, prefix, (servico, ...args) => servico === 'sqs' ? { Attributes: {} } : consultar(servico, ...args)), /Fila real/);
});


test('preparação não assume development quando o ambiente não foi informado', () => {
  const { main } = require('./preparar-ambiente-execucao.cjs');
  assert.throws(() => main({ env: { TERRAFORM_ACTION: 'destroy' }, consultarAws: () => assert.fail('Não deve consultar AWS') }), /Ambiente SSM/);
});
