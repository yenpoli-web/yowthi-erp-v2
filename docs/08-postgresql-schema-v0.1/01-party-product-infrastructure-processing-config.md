# PostgreSQL Schema v0.1 — Part 1

## `party`

Separate masters:
- `party.suppliers`
- `party.farmers`
- `party.employees`
- `party.customers`
- `party.outsourced_vendors`

No Universal Party table required in v0.1.

Localized visible names:
- `name_zh_tw`
- `name_th_th`

At least one must be present.

Supplier/Farmer/Employee/Outsourced Vendor fields:
- bilingual name
- bank name
- bank account
- phone
- address
- active
- lifecycle metadata

Customer:
- bilingual name
- phone
- active
- lifecycle metadata

## `infrastructure`

### `containers`
- id
- bilingual name
- tare_weight exact numeric
- active/lifecycle

### `warehouses`
- id
- optional code
- bilingual name
- active/lifecycle

### `storage_locations`
- id
- warehouse_id FK
- optional location code
- bilingual name
- active/lifecycle

## `product`

### `procurement_products`
- id
- bilingual name
- unit_code
- optional default_storage_location_id
- active/lifecycle

### `sales_product_groups`
- id
- bilingual name
- active/lifecycle
- no inventory fields

### `sales_products`
- id
- sales_product_group_id
- bilingual name
- pricing_basis
- packaging_weight nullable
- sales_weight conditional
- optional default storage
- active/lifecycle

Pricing basis:
- WEIGHT_BASED_UNIT → sales_weight required
- UNIT_BASED → sales_weight null

Sales Product does not store Origin.

## `processing_config`

### `processing_routes`
- id
- procurement_product_id
- bilingual name
- active/lifecycle

### `processing_route_versions`
- route FK
- version_number
- DRAFT / VALIDATED / ACTIVE / RETIRED
- one active version per Route
- version number unique and not reused

### `route_input_configs`
- route version PK/FK
- uses_container
- container_id
- default_container_count
- optional default storage

### `process_materials`
- route version FK
- bilingual name
- container settings
- optional default storage
- concurrency/lifecycle

### `processing_modules`
- route version FK
- bilingual name
- execution_mode
- input_process_material_id nullable
  - null means route Procurement Product
- negative_inventory_policy
- no next_module_id

### `processing_module_outputs`
- module FK
- output_sequence
- output_kind = PROCESS_MATERIAL / SALES_PRODUCT
- typed output FK
- default_wage_rate
- unique module + sequence

Module graph is connected through Material outputs → next module inputs.
