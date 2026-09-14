# 04 - Contratos REST

## 1. Convenções gerais

- Base paths: `/api/v1/proposals` e `/api/v1/contracts`.
- JSON em `camelCase`; valores de status em `snake_case`.
- Datas em ISO 8601 UTC, por exemplo `2026-09-14T21:30:00Z`.
- Dinheiro é número decimal JSON, nunca `double` no domínio.
- `Content-Type: application/json` para requests e respostas de sucesso.
- Erros usam `application/problem+json` e RFC 7807.
- IDs são UUIDs.
- Endpoints de criação retornam `Location`.
- Requests aceitam `traceparent`; respostas de erro incluem `traceId`.

## 2. ProposalService

### 2.1 Criar proposta

`POST /api/v1/proposals`

Request:

```json
{
  "customerId": "CUSTOMER-123",
  "productCode": "AUTO_BASIC",
  "insuredAmount": 75000.00,
  "monthlyPremium": 189.90
}
```

Resposta `201 Created`:

```json
{
  "id": "7b0eb9ec-0d20-4481-b24f-aa399f27d9fe",
  "customerId": "CUSTOMER-123",
  "productCode": "AUTO_BASIC",
  "insuredAmount": 75000.00,
  "monthlyPremium": 189.90,
  "status": "under_review",
  "createdAtUtc": "2026-09-14T21:30:00Z",
  "updatedAtUtc": "2026-09-14T21:30:00Z",
  "version": 1
}
```

Headers relevantes:

```text
Location: /api/v1/proposals/7b0eb9ec-0d20-4481-b24f-aa399f27d9fe
```

Falhas: `400 validation_failed`.

### 2.2 Consultar proposta por ID

`GET /api/v1/proposals/{id}`

Resposta `200 OK`: mesmo modelo de proposta acima.

Falhas:

- `400 validation_failed` para UUID malformado.
- `404 proposal_not_found` para UUID válido ausente.

Esse endpoint é público no contrato v1 e também é usado pelo `ContractService`.

### 2.3 Listar propostas

`GET /api/v1/proposals?status=approved&page=1&pageSize=20`

Todos os parâmetros são opcionais. `status` aceita `under_review`, `approved` ou `rejected`.

Resposta `200 OK`:

```json
{
  "items": [
    {
      "id": "7b0eb9ec-0d20-4481-b24f-aa399f27d9fe",
      "customerId": "CUSTOMER-123",
      "productCode": "AUTO_BASIC",
      "insuredAmount": 75000.00,
      "monthlyPremium": 189.90,
      "status": "approved",
      "createdAtUtc": "2026-09-14T21:30:00Z",
      "updatedAtUtc": "2026-09-14T21:35:00Z",
      "version": 2
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1
}
```

Falhas: `400 validation_failed` para status, página ou tamanho inválidos.

### 2.4 Alterar status

`PATCH /api/v1/proposals/{id}/status`

Request:

```json
{
  "status": "approved"
}
```

Somente `approved` ou `rejected` são aceitos como destino. `under_review` nunca é aceito neste endpoint.

Resposta `200 OK`: proposta completa já atualizada.

Falhas:

- `400 validation_failed` para ID/status inválido.
- `404 proposal_not_found`.
- `409 invalid_status_transition`.
- `409 proposal_concurrency_conflict`.

## 3. ContractService

### 3.1 Criar contratação

`POST /api/v1/contracts`

Request:

```json
{
  "proposalId": "7b0eb9ec-0d20-4481-b24f-aa399f27d9fe"
}
```

Resposta `201 Created`:

```json
{
  "id": "97fabcf7-6dc4-4896-bbe1-274ce749217f",
  "proposalId": "7b0eb9ec-0d20-4481-b24f-aa399f27d9fe",
  "contractedAtUtc": "2026-09-14T21:40:00Z"
}
```

Headers relevantes:

```text
Location: /api/v1/contracts/97fabcf7-6dc4-4896-bbe1-274ce749217f
```

Falhas:

- `400 validation_failed`.
- `404 proposal_not_found`.
- `409 proposal_not_approved`.
- `409 contract_already_exists`.
- `502 proposal_service_invalid_response`.
- `503 proposal_service_unavailable`; pode incluir `Retry-After` quando aplicável.

### 3.2 Consultar contratação por ID

`GET /api/v1/contracts/{id}`

Resposta `200 OK`: modelo de contratação acima.

Falhas: `400 validation_failed` ou `404 contract_not_found`.

### 3.3 Consultar contratação por proposta

`GET /api/v1/contracts/by-proposal/{proposalId}`

Resposta `200 OK`: modelo de contratação.

Falhas: `400 validation_failed` ou `404 contract_not_found`.

Não é necessária uma listagem geral de contratos para cumprir o desafio.

## 4. ProblemDetails

Exemplo de proposta não aprovada:

```json
{
  "type": "https://errors.insurance.local/proposal-not-approved",
  "title": "Proposal is not approved",
  "status": 409,
  "detail": "Only approved proposals can be contracted.",
  "instance": "/api/v1/contracts",
  "code": "proposal_not_approved",
  "traceId": "00-f4c8d8c7d7d59f1d58b40ce4cb134c7a-9c638a7b7b0a8f50-01"
}
```

Exemplo de validação:

```json
{
  "type": "https://errors.insurance.local/validation-failed",
  "title": "Request validation failed",
  "status": 400,
  "detail": "One or more fields are invalid.",
  "code": "validation_failed",
  "traceId": "00-f4c8d8c7d7d59f1d58b40ce4cb134c7a-9c638a7b7b0a8f50-01",
  "errors": {
    "insuredAmount": ["The value must be greater than zero."]
  }
}
```

Mensagens podem ser em inglês porque o código e o OpenAPI são técnicos; os campos e códigos acima são estáveis.

## 5. Tabela consolidada de status HTTP

| Status | Uso |
| --- | --- |
| 200 | Consulta ou alteração bem-sucedida |
| 201 | Recurso criado |
| 400 | Forma ou valores de entrada inválidos |
| 404 | Proposta/contratação não encontrada |
| 409 | Estado de domínio, concorrência ou duplicidade impede operação |
| 502 | Dependência respondeu fora do contrato |
| 503 | Dependência necessária indisponível |
| 500 | Falha interna não prevista |

## 6. Contrato consumido pelo ProposalHttpGateway

O adaptador do `ContractService` consome somente estes campos da resposta do `GET /api/v1/proposals/{id}`:

```json
{
  "id": "7b0eb9ec-0d20-4481-b24f-aa399f27d9fe",
  "status": "approved"
}
```

Campos adicionais são ignorados. `id` divergente, status desconhecido, JSON inválido ou corpo vazio são resposta inválida. O adaptador não referencia os DTOs, assemblies ou enums do `ProposalService`.

## 7. OpenAPI

Cada API deve gerar um documento OpenAPI que:

- inclua exemplos e todos os códigos de resposta deste arquivo;
- serialize os três status exatamente como documentado;
- declare UUID, decimal e `date-time` corretamente;
- identifique versão `v1` no path;
- descreva explicitamente a ausência de autenticação no escopo do teste;
- permaneça acessível em desenvolvimento, com JSON em `/swagger/v1/swagger.json`.

A verificação automatizada que impede divergência entre endpoints implementados e este contrato vive em
`ProposalContractAndReadinessTests` e `ContractContractAndReadinessTests`: elas comparam paths, códigos de resposta e os
valores de status do documento OpenAPI com as tabelas acima.

