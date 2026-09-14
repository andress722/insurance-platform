# 01 - Escopo, requisitos e decisões

## 1. Objetivo

Construir uma pequena plataforma de seguros com dois microserviços: um gerencia propostas e outro registra contratações. A implementação deve demonstrar arquitetura hexagonal, DDD tático, SOLID, persistência relacional, APIs REST e testes automatizados, sem transformar um teste técnico em uma plataforma maior que o problema.

## 2. Rastreabilidade do enunciado

| Requisito do PDF | Realização planejada | Evidência esperada |
| --- | --- | --- |
| Criar proposta | `POST /api/v1/proposals` | Testes de domínio, aplicação e integração |
| Listar propostas | `GET /api/v1/proposals` paginado | Teste de filtro e paginação |
| Alterar status | `PATCH /api/v1/proposals/{id}/status` | Testes da máquina de estados |
| Contratar somente aprovada | `POST /api/v1/contracts` + consulta remota | Cenários aprovado, em análise e rejeitado |
| Guardar ID e data | Agregado `Contract` e tabela `contracts` | Migration e teste de repositório |
| Comunicação entre serviços | HTTP REST síncrono | `IProposalGateway` + adaptador `HttpClient` |
| C#/.NET 8+ | `net8.0` | Build da solution |
| Banco relacional | PostgreSQL | Dois bancos e migrations EF Core |
| Arquitetura Hexagonal | Portas de entrada/saída e adaptadores | Testes de arquitetura |
| Clean Architecture, DDD e SOLID | Domínio isolado e casos de uso pequenos | Dependências de projetos e testes |
| Docker - bônus | Dockerfile por API + Compose | Subida local completa |
| Testes automatizados | Unidade, integração, arquitetura e smoke | `dotnet test` e script de smoke |
| Diagrama - bônus | Diagramas Mermaid versionados | `docs/02-ARCHITECTURE.md` |

## 3. Decisões complementares ao enunciado

O PDF não define todos os detalhes necessários para uma implementação determinística. As decisões abaixo formam o contrato do MVP.

| Tema | Decisão | Motivo |
| --- | --- | --- |
| Idioma do código | Inglês | Convenção técnica e consistência com .NET |
| Idioma da documentação | Português | Aderência ao teste e facilidade de avaliação |
| Estado inicial | `under_review` | Uma proposta recém-criada ainda não foi decidida |
| Transições | `under_review -> approved` ou `under_review -> rejected` | Regras explícitas e auditáveis |
| Estados finais | `approved` e `rejected` | Evita alteração depois da decisão e elimina corrida no contrato |
| Cardinalidade | Uma contratação por proposta | Significado natural de contratação e proteção contra duplicidade |
| Data da contratação | Gerada pelo servidor em UTC | Evita adulteração e problemas de fuso |
| Consistência | Consulta HTTP no ato da contratação | A regra de elegibilidade exige resposta atual |
| Propriedade dos dados | Database-per-service | Preserva autonomia dos microserviços |
| IDs | UUID/`Guid` | Geração descentralizada e simples |
| Dinheiro | `decimal(18,2)`, valores positivos | Evita erro de ponto flutuante |
| Erros HTTP | RFC 7807, com `code` e `traceId` | Respostas previsíveis e observáveis |
| Exclusão | Não haverá exclusão no MVP | Não solicitada e cria implicações de auditoria |

## 4. Escopo funcional do MVP

### ProposalService

- Criar proposta com cliente, produto, capital segurado e prêmio mensal.
- Consultar proposta por ID, pois esse endpoint é necessário à integração.
- Listar propostas com paginação e filtro opcional por status.
- Aprovar ou rejeitar uma proposta em análise.
- Persistir e recuperar dados no banco próprio.

### ContractService

- Receber uma solicitação de contratação por `proposalId`.
- Detectar contratação já existente.
- Consultar o status publicado pelo `ProposalService`.
- Rejeitar proposta inexistente, ainda em análise ou rejeitada.
- Criar exatamente uma contratação para uma proposta aprovada.
- Consultar contratação por ID e por proposta.

## 5. Fora do escopo

- Interface web ou mobile.
- Autenticação, autorização e gestão de usuários.
- Cadastro completo de segurados, CPF/CNPJ ou documentos sensíveis.
- Cotação e cálculo atuarial.
- Pagamento, emissão de apólice, cancelamento ou endosso.
- Kubernetes, service mesh e cloud específica.
- Event sourcing.
- Saga distribuída; o fluxo não possui duas escritas distribuídas.
- Cache distribuído.

Esses itens não entram por iniciativa isolada: qualquer expansão exige uma decisão registrada neste documento.

## 6. Regras de negócio

| ID | Regra |
| --- | --- |
| BR-P-01 | Uma proposta nova sempre nasce `under_review`. |
| BR-P-02 | `customerId`, `productCode`, `insuredAmount` e `monthlyPremium` são obrigatórios. |
| BR-P-03 | `insuredAmount` e `monthlyPremium` devem ser maiores que zero. |
| BR-P-04 | Somente propostas `under_review` podem mudar de status. |
| BR-P-05 | Os únicos destinos válidos são `approved` e `rejected`. |
| BR-P-06 | Repetir o mesmo status final é uma operação idempotente e devolve o estado atual. |
| BR-C-01 | Somente uma proposta `approved` pode ser contratada. |
| BR-C-02 | Cada proposta pode possuir no máximo uma contratação. |
| BR-C-03 | `contractedAtUtc` é definido pelo relógio do servidor. |
| BR-C-04 | Indisponibilidade do serviço de propostas não autoriza contratação. |
| BR-C-05 | A restrição única do banco é a proteção final contra requisições concorrentes. |

## 7. Requisitos não funcionais do MVP

- Toda operação assíncrona aceita e propaga `CancellationToken`.
- Nenhum segredo fica em código, imagem Docker ou arquivo versionado.
- Logs são estruturados e nunca incluem corpo completo da proposta.
- APIs expõem health checks de vivacidade e prontidão.
- O cliente HTTP tem timeout curto e política limitada de retry apenas para `GET`.
- Migrations são versionadas; `EnsureCreated` é proibido.
- Todas as coleções REST têm limite máximo de página.
- A aplicação responde com tipos de erro estáveis, sem stack trace externo.
- Builds e testes devem ser determinísticos.

## 8. Cenários de aceite principais

### Caminho feliz

1. Dada uma proposta válida recém-criada, seu status é `under_review`.
2. Quando ela é aprovada, a consulta por ID retorna `approved`.
3. Quando o cliente solicita a contratação, o serviço consulta a proposta e cria o contrato.
4. A resposta é `201 Created`, contém `contractedAtUtc` e um `Location` válido.

### Proteções

- Proposta em análise: contratação retorna `409 proposal_not_approved`.
- Proposta rejeitada: contratação retorna `409 proposal_not_approved`.
- Proposta inexistente: contratação retorna `404 proposal_not_found`.
- Proposta já contratada: nova solicitação retorna `409 contract_already_exists`.
- Serviço de propostas indisponível ou em timeout: retorna `503 proposal_service_unavailable` e não grava contrato.
- Duas solicitações concorrentes: uma cria; a outra recebe conflito; existe uma única linha no banco.

## 9. Bônus controlado

Depois de o MVP estar integralmente verde, o `ProposalService` pode publicar `ProposalStatusChangedV1` via RabbitMQ usando Outbox transacional. Esse evento serve para integração e demonstração, mas **não substitui** a consulta síncrona que autoriza a contratação. A ausência do broker não pode impedir o funcionamento obrigatório das APIs.

