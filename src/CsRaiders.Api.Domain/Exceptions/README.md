# Exceptions

## Overview
Domain-layer exception definitions providing structured domain failure representations with machine-readable error codes.

## Interface
- [DomainException.cs:DomainException]: Base domain exception thrown upon business invariant violations carrying a machine-readable `Code` and message.
- [DomainException.cs:DomainException.Code]: Machine-readable error code identifying the specific domain rule violation.

## Invariants
- [DomainException.cs:DomainException.Code]: Must represent an unambiguous machine-readable domain error key (e.g., `unknown_permission`, `student_role_forbidden_permission`).

## Side Effects
- None

## Verification
- Run: `dotnet test`
