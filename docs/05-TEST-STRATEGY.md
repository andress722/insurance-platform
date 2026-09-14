# 05 - Estratégia de testes

## 1. Objetivo

Os testes devem provar as regras relevantes e os limites arquiteturais, não apenas aumentar cobertura. O conjunto precisa detectar regressões em transições de status, elegibilidade, duplicidade, persistência, serialização e comunicação entre serviços.

## 2. Pirâmide planejada

| Nível | Escopo | Dependências reais | Velocidade esperada |
| --- | --- | --- | --- |
| Unidade de domínio | Agregados e value objects | Nenhuma | Milissegundos |
| Unidade de aplicação | Casos de uso e mapeamento de erros | Portas simuladas | Milissegundos |
| Arquitetura | Referências entre assemblies | Assemblies compilados | Segundos |
| Integração | API, EF Core, migrations, PostgreSQL e adaptador HTTP | Testcontainers/stub HTTP | Segundos |
| Smoke end-to-end | Dois serviços e dois bancos | Docker Compose | Até poucos minutos |

Ferramentas recomendadas: xUnit, biblioteca de assertions legível, NSubstitute ou mocks manuais, `WebApplicationFactory`, Testcontainers for .NET e um stub HTTP controlável. Fixar versões compatíveis com `net8.0` no gerenciamento central de pacotes.

## 3. Convenções

- Nome: `Method_Scenario_ExpectedResult` ou `Given_When_Then`, adotando um padrão único.
- Um teste descreve uma regra principal.
- Arrange/Act/Assert visível; helpers não escondem a ação testada.
- Relógio e gerador de ID controláveis; nenhum teste depende de `UtcNow` real.
- Sem `Thread.Sleep`; sincronização por sinais, polling com timeout curto ou primitivas apropriadas.
- Sem dependência da ordem dos testes.
- Integrações criam e removem seus próprios dados.
- Não comparar textos humanos quando há código de erro estável.
- Testes de erro conferem HTTP status, `code` e ausência de escrita indevida.

## 4. ProposalService - unidade de domínio

| ID | Cenário | Resultado esperado |
| --- | --- | --- |
| PU-01 | Criar com dados válidos | ID preenchido, status `UnderReview`, datas e versão 1 |
| PU-02 | `customerId` vazio ou longo | Falha de validação |
| PU-03 | `productCode` vazio ou longo | Falha de validação |
| PU-04 | Valores monetários zero/negativos | Falha de validação |
| PU-05 | Valores com mais de duas casas | Regra definida aplicada sem perda silenciosa; preferir rejeição |
| PU-06 | Aprovar em análise | Status aprovado, data atualizada, versão incrementada |
| PU-07 | Rejeitar em análise | Status rejeitado, data atualizada, versão incrementada |
| PU-08 | Repetir mesma decisão final | Sucesso idempotente, sem mudar data/versão |
| PU-09 | Aprovada -> rejeitada | `invalid_status_transition` |
| PU-10 | Rejeitada -> aprovada | `invalid_status_transition` |

## 5. ProposalService - aplicação

| ID | Caso de uso | Prova |
| --- | --- | --- |
| PA-01 | Criar | Persiste uma vez e confirma UoW |
| PA-02 | Consultar ausente | `proposal_not_found` |
| PA-03 | Listar | Propaga filtro/paginação e mapeia envelope |
| PA-04 | Alterar ausente | Não confirma transação |
| PA-05 | Alterar válido | Usa comportamento do agregado e confirma |
| PA-06 | Conflito de concorrência | Mapeia para `proposal_concurrency_conflict` |
| PA-07 | Cancelamento | Propaga token às portas |
| PA-08 | Duas decisões iguais concorrentes | Uma grava e ambas devolvem o mesmo estado final |

## 6. ContractService - unidade de domínio e aplicação

| ID | Cenário | Resultado esperado |
| --- | --- | --- |
| CU-01 | Criar agregado válido | ID, proposalId e data corretos |
| CU-02 | `proposalId` vazio | Falha de validação |
| CA-01 | Contrato local já existe | `contract_already_exists`; gateway não chamado |
| CA-02 | Proposta não existe | `proposal_not_found`; nada persistido |
| CA-03 | Proposta em análise | `proposal_not_approved`; nada persistido |
| CA-04 | Proposta rejeitada | `proposal_not_approved`; nada persistido |
| CA-05 | Proposta aprovada | Cria e confirma exatamente uma vez |
| CA-06 | Gateway indisponível | `proposal_service_unavailable`; nada persistido |
| CA-07 | Resposta remota inválida | `proposal_service_invalid_response`; nada persistido |
| CA-08 | Violação única no commit | `contract_already_exists` |
| CA-09 | Cancelamento | Propaga token ao repositório, gateway e UoW |

## 6.1 ContractService - pipeline de resiliência

Estes cenários montam o pipeline real de `AddContractInfrastructure` e substituem apenas o handler de socket, de modo
que retry, circuit breaker, timeout por tentativa e validação de opções são efetivamente exercitados.

| ID | Cenário | Resultado esperado |
| --- | --- | --- |
| CR-01 | `5xx` no `GET` | Uma retentativa, dois envios, `proposal_service_unavailable` |
| CR-02 | `RetryCount=0` | Um único envio |
| CR-03 | `404` e `4xx` inesperado | Sem retentativa; `proposal_not_found` e `proposal_service_invalid_response` |
| CR-04 | Dependência lenta | Timeout de tentativa dispara, há uma retentativa e o total respeita o orçamento |
| CR-05 | Falhas acima do limiar | Circuito abre e a chamada seguinte não chega à dependência |
| CR-06 | Cancelamento do chamador | Sem retentativa; exceção propaga |
| CR-07 | Configuração inválida | `OptionsValidationException` no startup |

## 6.2 Contrato publicado e prontidão

Executados sem banco, com `WebApplicationFactory`:

- O documento OpenAPI de cada serviço expõe exatamente os paths e os códigos de resposta de `docs/04`; qualquer endpoint
  novo ou código divergente quebra o teste.
- O schema de status serializa exatamente `under_review`, `approved` e `rejected`, e o vocabulário do wire cobre todos os
  membros do enum de domínio.
- Falhas geradas pelo próprio pipeline (404 de rota, 405) devolvem ProblemDetails com `code` próprio e `traceId`, nunca
  `internal_error`.
- `/health/live` responde 200 e `/health/ready` responde 503 com o banco inacessível, sem vazar credenciais.

## 7. Testes de integração do ProposalService

- Aplicar migrations em PostgreSQL vazio.
- Criar via API e ler a linha por API/repositório.
- Validar serialização `under_review`, `approved` e `rejected`.
- Validar precisão `numeric(18,2)` sem conversão binária.
- Listar com filtro, ordenação e paginação determinística.
- Confirmar `AsNoTracking` indiretamente por contexto limpo ou teste de repositório.
- Aprovar e rejeitar pelos endpoints.
- Simular duas atualizações sobre a mesma versão e confirmar um único vencedor.
- Para duas decisões concorrentes opostas, confirmar um sucesso e um conflito; para duas decisões iguais, confirmar efeito único e resposta idempotente.
- Conferir ProblemDetails para validação, não encontrado e conflito.
- Conferir health readiness com banco acessível e indisponível.

## 8. Testes de integração do ContractService

- Aplicar migrations em PostgreSQL vazio.
- Configurar stub do `ProposalService` para cada status, 404, 500, timeout e JSON inválido.
- Verificar mapeamento de cada resposta remota.
- Criar contrato apenas quando o stub devolve `approved`.
- Consultar por ID e por proposta.
- Disparar duas requisições concorrentes e confirmar uma linha e um conflito.
- Confirmar que timeout/5xx não geram linha no banco.
- Verificar que retries acontecem somente nos GETs elegíveis e têm limite.
- Conferir propagation de `traceparent` quando suportado pelo pipeline padrão.

## 9. Testes de arquitetura

Os testes carregam os assemblies e falham se:

- `*.Domain` referencia EF Core, ASP.NET Core, Npgsql ou `*.Infrastructure`.
- `*.Application` referencia `*.Api` ou `*.Infrastructure`.
- Um serviço referencia assembly do outro.
- Controllers acessam `DbContext` ou repositórios diretamente.
- Adaptadores de saída são implementados dentro de `Domain`.

Também deve existir um teste simples que garanta que todas as classes de caso de uso estejam no assembly `Application` e que implementações de repositório estejam em `Infrastructure`.

## 10. Smoke end-to-end

Executado depois de `docker compose up --build --wait`:

1. Aguardar `/health/ready` dos dois serviços.
2. Criar proposta; conferir `201` e `under_review`.
3. Tentar contratar; conferir `409 proposal_not_approved`.
4. Aprovar proposta; conferir `200` e `approved`.
5. Contratar; conferir `201`, `Location` e data UTC.
6. Consultar o contrato por ID e por proposta.
7. Tentar contratar novamente; conferir `409 contract_already_exists`.
8. Criar e rejeitar outra proposta; conferir que ela não pode ser contratada.

O script termina com exit code diferente de zero na primeira divergência e imprime apenas dados suficientes para diagnóstico.

## 11. Gates de qualidade

Um merge só é elegível quando:

```bash
dotnet format InsurancePlatform.sln --verify-no-changes
dotnet build InsurancePlatform.sln -c Release --no-restore
dotnet test InsurancePlatform.sln -c Release --no-build
```

- Zero erro e zero teste falho.
- Warnings do código próprio são tratados como erro; warnings inevitáveis de ferramentas devem ser documentados, não globalmente ignorados.
- Cobertura é reportada para Domain/Application. Meta orientativa: pelo menos 90% de linhas nesses assemblies e 100% das regras BR-P/BR-C exercitadas.
- A aprovação depende dos cenários, mesmo que a porcentagem já tenha sido alcançada.

## 12. Matriz requisito -> teste

| Regra | Unidade | Integração | Smoke |
| --- | --- | --- | --- |
| Criar proposta em análise | PU-01, PA-01 | API + banco | Passo 2 |
| Listar propostas | PA-03 | filtro/paginação | Opcional |
| Alterar status | PU-06 a PU-10 | endpoint + concorrência | Passos 4 e 8 |
| Somente aprovada contrata | CA-02 a CA-07 | stub de todos os estados | Passos 3, 5 e 8 |
| Persistir ID/data | CU-01, CA-05 | contrato no PostgreSQL | Passos 5 e 6 |
| Sem duplicidade | CA-01, CA-08 | concorrência + unique | Passo 7 |
| Hexagonal | testes de arquitetura | composição da API | Build completo |
