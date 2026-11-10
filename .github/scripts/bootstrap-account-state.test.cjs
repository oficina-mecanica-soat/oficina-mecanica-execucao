const {test}=require('node:test');
const assert=require('node:assert/strict');
const {checkEmpty}=require('./bootstrap-account-state.cjs');
test('bootstrap rejeita recursos do projeto e respostas inválidas',()=>{
  assert.throws(()=>checkEmpty('VPC',[{Tags:[{Key:'Project',Value:'OficinaMecanica'}]}]),/bloqueado/);
  assert.throws(()=>checkEmpty('Lambda',[{FunctionName:'oficina-mecanica-auth-lambda-dev'}]),/bloqueado/);
  assert.throws(()=>checkEmpty('EKS',null),/inválida/);
  checkEmpty('Lambda',[{FunctionName:'MainMonitoringFunction'}]);
  checkEmpty('VPC',[{IsDefault:true}]);
});
