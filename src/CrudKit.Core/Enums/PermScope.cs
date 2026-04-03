namespace CrudKit.Core.Enums;

/// <summary>İzin kapsamı — hangi kayıtlara erişilebileceğini belirler.</summary>
public enum PermScope
{
    Own,           // Sadece kendi kayıtları
    Department,    // Departmanındaki kayıtlar
    All            // Tüm kayıtlar
}
