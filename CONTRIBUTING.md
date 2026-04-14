# Contributing to CrudKit

Thank you for your interest in contributing to CrudKit!

## Development Setup

```bash
# Clone
git clone https://github.com/suleymanozev/CrudKit.git
cd CrudKit

# Build
dotnet build CrudKit.slnx

# Run tests
dotnet test CrudKit.slnx

# Run integration tests with PostgreSQL (requires Docker)
dotnet test tests/CrudKit.Integration.Tests/
```

## Project Structure

```
src/
├── CrudKit.Core/                    # Attributes, interfaces, models (no dependencies)
├── CrudKit.EntityFrameworkCore/     # EF Core integration, repository, query
├── CrudKit.Api/                     # Minimal API layer, endpoint mapping, filters
└── CrudKit.Identity/                # ASP.NET Identity integration

tests/
├── CrudKit.Core.Tests/              # Unit tests for core
├── CrudKit.EntityFrameworkCore.Tests/ # EF integration tests
├── CrudKit.Api.Tests/               # API endpoint tests + security audit
├── CrudKit.Identity.Tests/          # Identity context tests
└── CrudKit.Integration.Tests/       # Provider-agnostic tests (SQLite + PostgreSQL)
```

## Guidelines

- All source code comments and XML docs must be in **English**
- Follow existing code style (check `.editorconfig` if available)
- Write tests for new features — aim for both unit and integration coverage
- Security-sensitive changes require security test coverage
- Keep `[CrudEntity]` as the single entry point for entity discovery

## Pull Request Process

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Write tests first (TDD encouraged)
4. Implement the feature
5. Ensure all tests pass (`dotnet test CrudKit.slnx`)
6. Ensure integration tests pass with Docker (`dotnet test tests/CrudKit.Integration.Tests/`)
7. Update documentation if needed
8. Submit a pull request

## Reporting Issues

- Use GitHub Issues
- Include: expected behavior, actual behavior, steps to reproduce
- For security vulnerabilities, email directly (do not open public issues)
