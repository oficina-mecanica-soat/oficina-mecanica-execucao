const {test} = require('node:test');
const assert = require('node:assert/strict');
const {destroyConfig, verifyPlan} = require('./prepare-destroy.cjs');
test('destroy AWS não inclui contratos SSM nem modifica state', () => {
  const state = {resources:[{mode:'managed',type:'aws_vpc',name:'this',provider:'provider["registry.terraform.io/hashicorp/aws"]'}]};
  const before = JSON.stringify(state);
  assert.match(destroyConfig(state,'us-east-1',()=>assert.fail()), /provider "aws"/);
  assert.equal(JSON.stringify(state),before);
});
test('Kubernetes exige ausência comprovada do EKS e usa removed declarativo', () => {
  const state = {resources:[{mode:'data',type:'aws_eks_cluster',instances:[{attributes:{name:'test'}}]}, {mode:'managed',type:'kubernetes_namespace_v1',name:'this'}]};
  assert.throws(()=>destroyConfig(state,'us-east-1',()=>{throw Error('AccessDenied')}),/AccessDenied/);
  assert.match(destroyConfig(state,'us-east-1',name=>assert.equal(name,'test')),/from = kubernetes_namespace_v1.this/);
  assert.match(destroyConfig(state,'us-east-1',()=>{}),/destroy = false/);
  assert.throws(()=>destroyConfig({resources:[state.resources[1]]},'us-east-1',()=>{}),/sem nome/);
});
test('plano rejeita criação, atualização e substituição', () => {
  for(const actions of [['create'],['update'],['delete','create']]) assert.throws(()=>verifyPlan({resource_changes:[{address:'test',change:{actions}}]}),/proibida/);
  for(const actions of [['no-op'],['delete'],['forget']]) verifyPlan({resource_changes:[{change:{actions}}]});
});
