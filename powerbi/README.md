# Power BI report — `GloveOps Plant & Sales.pbix`

The database already contains a **star schema** in the `bi` schema (built by `database/04_bi_star_schema.sql`, all data fictional):

| Table | Type | Rows | Grain |
|---|---|---|---|
| `bi.FactProduction` | fact | ~8,000 | day × plant × line |
| `bi.FactSales` | fact | ~5,700 | order line per weekday |
| `bi.vSampleRequest` | fact (view, live) | live | one sample request |
| `bi.DimDate` | dimension | 669 | day, 2025-01-01 to 2026-10-31 |
| `bi.DimPlant` | dimension | 3 | Tainan, Jiangsu, Binh Duong |
| `bi.DimProduct` | dimension | 8 | SKU (Stock Keeping Unit) with material and category |
| `bi.DimCustomer` | dimension | 10 | customer and country |
| `bi.UserPlant` | security | 3 | user email → plant (for RLS, Row-Level Security) |

```
            DimDate ──┐
DimPlant ─────────────┼── FactProduction
            DimProduct┤
DimCustomer ──────────┼── FactSales
                      └── vSampleRequest
UserPlant ── DimPlant      (RLS filter flows Plant → facts)
```

## 1. Build the report (Power BI Desktop, free, Windows)
1. **Get data → SQL Server.** Server `DIVAKARLAPTOP\DEVELOPERMSSQL`, database `DivakarGloveOps`, **Import** mode. Choose *Windows* credentials.
2. Select the `bi.*` tables and view listed above → **Transform Data** (Power Query): set `DimDate[Date]` to type Date. No other cleaning is needed.
3. **Model view.** Create single-direction relationships (many → one):
   - `FactProduction[DateKey]` → `DimDate[DateKey]`, `[PlantKey]` → `DimPlant[PlantKey]`, `[ProductKey]` → `DimProduct[ProductKey]`
   - `FactSales[DateKey]` → `DimDate`, `[CustomerKey]` → `DimCustomer`, `[ProductKey]` → `DimProduct`
   - `vSampleRequest[DateKey]` → `DimDate`, `[CustomerKey]` → `DimCustomer`, `[ProductKey]` → `DimProduct`
   - `UserPlant[PlantKey]` → `DimPlant[PlantKey]`
   - Mark `DimDate` as the **date table** (Table tools → Mark as date table → `Date`).
4. Create a `_Measures` table (Enter data → empty table) and paste the measures from [`measures.dax`](measures.dax).

## 2. Report pages
| Page | Visuals |
|---|---|
| **Executive overview** | Cards: Sales YTD (Year to Date), Sales vs LY (Last Year) %, Output YTD, Defect Rate %. Line chart: Sales USD by YearMonth. Filled map or bar: Sales by Country. Slicers: Year, Material |
| **Production & quality** | Clustered column: Output by Plant & Line. Line: Defect Rate % by YearMonth per plant. Matrix: Plant × Line with Output, Defects, Defect Rate %, Downtime h. Conditional formatting: Defect Rate % > 2% shown in red |
| **Sample requests** (live) | Cards: Requests, Approved %, Avg Lead Hours. Donut: by Status. Bar: Boxes by Material |

## 3. Row-Level Security
1. Modeling → **Manage roles** → new role `PlantManager`, table `UserPlant`, filter: `[UserEmail] = USERPRINCIPALNAME()`.
2. Make sure the `UserPlant → DimPlant` relationship has **Apply security filter in both directions** ticked, so the filter reaches the facts.
3. Test: Modeling → **View as** → role `PlantManager` + *Other user* `linh@gloveops.demo` → only Binh Duong data is shown.

## 4. Publish & refresh (if you have a Power BI account)
- Publish to the Power BI Service. Install the **on-premises data gateway** so the cloud can refresh from the local SQL Server, and schedule a daily refresh.
- No account? Save the `.pbix` in this folder and add screenshots to `docs/screenshots/`. That's enough for an interview demo.

## 5. What to say in the interview
> "I modelled a star schema in SQL Server, with production and sales facts and date, plant, product and customer dimensions, and kept heavy logic in SQL. In Power BI I use Import mode, a marked date table, and DAX measures like defect rate and year-over-year sales. Plant managers only see their own plant through row-level security driven by a user-to-plant mapping table. The sample-request page reads a live view from the GloveOps app, so management sees approval lead times next to production KPIs (Key Performance Indicators)."
