# Davids.Services.Products

Clean Architecture `.NET 10` product catalog and inventory microservice for the Davids platform, backed by SQL Server (`productsdb`), dual-realm RS256 OIDC verification, and RabbitMQ transactional Outbox/Inbox synchronization.

## Capabilities

- Public storefront catalog endpoints (`GET /products`, `GET /products/{productId}`) serving active products with search and category filtering
- Workforce catalog administration (`GET /products/admin`, `POST /products`, `PUT /products/{productId}`, `POST /products/{productId}/stock`, `GET /products/change-audits`) gated by `AdminBearer` RBAC (`admin`, `support`, `auditor`)
- Idempotent `orders.order_completed` RabbitMQ consumer decrementing product stock and recording audited inventory changes
