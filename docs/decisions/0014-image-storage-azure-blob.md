# ADR-0014: Product images in Azure Blob Storage, uploaded through the API

- Status: **Accepted** for Azure Blob Storage. **User approval of the Azure Storage dependency recorded on 2026-11-05.** It is not in the original CLAUDE.md stack list.
  - The upload-through-API approach is the Architect's recommendation, accepted with the document set.
  - Anonymous public read access on the `catalog-images` container (Q-13) is **Accepted** (user decision 2026-11-06). Its scope covers product images, brand logos, category images and banner images (D-A4, user decision 2026-11-06).
  - At most 6 images per product (D-A2, user decision 2026-11-06).
  - The non-production storage account is provisioned by the user (D-12, user decision 2026-11-06).
- Date: 2026-11-05 (amended 2026-11-06; §8 accepted risks added 2026-11-07)
- Related: architecture §2, §7.4, §9, §11; database model §1, §3.6; requirements BR-30, AC-A10

## Context

Product, brand, category and banner images need durable storage, served efficiently to the public. Only admins upload. The original stack lists no storage service. The user approved Azure Blob Storage.

There are two options for upload:

- **A. Through the API:** a multipart upload to an admin endpoint. The API validates the file and writes the blob.
- **B. Direct to Blob with a SAS:** the API issues a short-lived write SAS, the browser uploads directly, and the API then confirms.

## Decision

1. **Storage:**
   - A StorageV2 account (LRS, HTTPS only, TLS 1.2 or later).
   - Container `catalog-images` for public images.
   - Container `dataprotection-keys`, private, for ASP.NET Core Data Protection keys (ADR-0010).
2. **Database:**
   - Stores only `BlobPath` (relative, server-generated: `products/{productId}/{guid}.{ext}`, or the prefixes `brands/`, `categories/`, `banners/`).
   - Stores metadata: `ContentType`, `SizeBytes`, `Width`, `Height`, `OriginalFileName` (display only), `AltText`, `SortOrder`, `IsPrimary`, `CreatedBy`.
   - Public URLs are built as `Storage:Images:PublicBaseUrl` + `/` + `BlobPath`, so a CDN can be added by changing configuration only.
3. **Upload goes through the API (option A):** `POST /api/v1/admin/products/{id}/images` (multipart). It requires `AdminOnly` and antiforgery, and is limited to 5 MB per file and 6 images per product (D-A2). A brand, category or banner has one image, replaced on upload.
   - **Validation before storing:** the server validates size and file signature (JPEG, PNG or WebP; no SVG) against the declared type before anything is stored. With a SAS, the file would already be in the container before validation.
   - **No credential issuance:** the API writes with its **Managed Identity** (*Storage Blob Data Contributor* role). No SAS tokens, account keys or storage CORS rules are needed.
   - **Small, low volume:** a few admin uploads of files up to 5 MB, so the extra bandwidth through App Service is negligible.
   - **Single transaction boundary:** upload the blob, insert the row, and delete the blob again if the insert fails.
   - *Revisit option B* if files over about 20 MB or high upload volume appear.
4. **Blob properties:** written with the correct `Content-Type` and `Cache-Control: public, max-age=31536000, immutable`. File names are unique GUIDs, so a blob is never overwritten.
5. **Public read (Q-13, approved 2026-11-06):** anonymous **blob-level** read on `catalog-images`, with container listing disabled.
   - Only catalogue images are public. No other blob is public: `dataprotection-keys` and any future container are private, and the API writes nothing to `catalog-images` other than validated catalogue display images.
   - The storage account must allow blob public access so that this one container can be set to blob-level access. Every other container keeps access level *private*.
   - An image is readable by its unguessable URL as soon as it is uploaded, including images of Draft products. Admins must not upload images that are not meant to be public.
   - **Public-read scope (D-A4, user decision 2026-11-06):** product images (`products/`), brand logos (`brands/`), category images (`categories/`) and banner images (`banners/`). All four are stored in `catalog-images` and are publicly readable. Nothing else is written to that container (`docs/requirements/0002-admin-requirements.md` BR-A-27).
   - If organisation policy later forbids anonymous access, the fallback is Azure Front Door or CDN with private origin access. A new infrastructure decision would be needed then.
6. **Code:**
   - `IImageStorage` (Application) is implemented by `AzureBlobImageStorage` (Infrastructure), using `Azure.Storage.Blobs` and `Azure.Identity` (`DefaultAzureCredential`).
   - Tests use an in-memory fake.
   - Local development uses **Azurite started with `npx`**, because there is no Docker (ADR-0013).
7. **Configuration:**

   | Key | Notes |
   |---|---|
   | `Storage:Images:ServiceUri` | |
   | `Storage:Images:Container` | `catalog-images` |
   | `Storage:Images:PublicBaseUrl` | |
   | `Storage:Images:ConnectionString` | Local Azurite only (`UseDevelopmentStorage=true`); not a secret |

   In Azure there are no secrets, only Managed Identity.
8. **Deletion:** delete the row, then the blob. Failures are logged. A scheduled cleanup of orphan blobs is deferred.
   - **Accepted (P4-06, Manager 2026-11-07):** an orphan blob also remains when the process dies between the blob write and the database commit (no compensation runs), and in the ambiguous-commit case (the commit reaches SQL Server but the client sees a failure, so the compensating delete may remove the blob of a committed row, or the row may be missing for a stored blob). Both are rare, logged where possible, and covered by the deferred orphan cleanup; no further mitigation in this release.
   - **Accepted (P4-06, Manager 2026-11-07):** the product write lock (`IProductWriteLock`, an `UPDLOCK` on the product row inside the transaction) is held while the blob is uploaded. A slow storage call therefore blocks other locked writes to the **same product** (image upload, image delete, and publish from P4-12) until it finishes, at most until the waiting request's SQL command timeout. Other products are not affected. Accepted because uploads are few and low volume (AS-A-04).

## Consequences

- New packages: `Azure.Storage.Blobs` and `Azure.Identity`. Later, `Azure.Extensions.AspNetCore.DataProtection.Blobs`.
- New Azure resources in Phase 5: the storage account, two containers, and a role assignment for the App Service identity. Provisioning production infrastructure requires explicit user approval.
- **Non-production storage account (D-12):** the user provisions it. The Developer must not create Azure resources. Until the user supplies its non-secret settings (`Storage:Images:ServiceUri`, `Storage:Images:PublicBaseUrl`), development and tests use Azurite and the in-memory fake.
- The CSP `img-src` must include the storage or CDN host from configuration.
- Seed and development data reference blob paths. The seeder uploads bundled placeholder images to Azurite when it is reachable.
