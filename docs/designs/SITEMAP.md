# Design Document Index

| Design Document | Date | Author | Status | Description |
| :--- | :--- | :--- | :--- | :--- |
| [Authentication & Session Management](authentication_and_session_management.md) | 2026-11-07 | ahmniab | Draft | Hardened authentication, session management, and RBAC subsystem with in-process sliding cache and single-session enforcement. |
| [Token-Embedded Authorization Bitmask Claims](token_embedded_authorization_bitmask_claims.md) | 2026-11-12 | ahmniab | Reviewed | Optimizes HTTP endpoint authorization by embedding effective permissions into a compact 64-bit unsigned scalar integer string claim in JWTs, evaluated locally via sub-microsecond bitwise operations with fail-fast enforcement, memoized refresh resolution, and zero database migrations. |
| [Real Email Delivery & Service Templating](real_email_delivery_and_service_templating.md) | 2026-11-14 | ahmniab | Implemented | Production-grade MailKit SMTP delivery, object-oriented service templating with HTML injection sanitization, non-blocking queueing extension, and durable dead-letter persistence. |
