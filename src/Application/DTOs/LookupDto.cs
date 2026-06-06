namespace PakistanAccountingERP.Application.DTOs;

public record LookupDto(int Id, string Name, string? Code = null);

public record ScenarioTypeDto(int Id, string Code, string? Description);

public record SubAccountTypeDto(int Id, int TypeId, string Code, string Name);

public record AccountTypeDto(int Id, string Code, string Name);
