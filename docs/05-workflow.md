## 5. CrudKit.Workflow

Opsiyonel paket. DB-driven akış, code-driven action'lar.

### 5.1 Dosya Yapısı

```
CrudKit.Workflow/
├── Engine/
│   ├── WorkflowEngine.cs
│   ├── ActionRegistry.cs
│   ├── ActionContext.cs
│   └── TimeoutChecker.cs
├── Models/
│   ├── WorkflowDefinition.cs
│   ├── StepDefinition.cs
│   ├── TransitionDefinition.cs
│   ├── WorkflowInstance.cs
│   ├── StepExecution.cs
│   ├── ApprovalRecord.cs
│   ├── WorkflowStatus.cs
│   └── StepStatus.cs
├── Steps/
│   ├── IStepExecutor.cs
│   ├── ActionStepExecutor.cs
│   ├── ApprovalStepExecutor.cs
│   ├── GatewayStepExecutor.cs
│   ├── ParallelStepExecutor.cs
│   ├── TimerStepExecutor.cs
│   └── WaitEventStepExecutor.cs
├── Persistence/
│   └── WorkflowDbContext.cs
├── Configuration/
│   └── WorkflowOptions.cs
└── Extensions/
    └── WorkflowServiceExtensions.cs
```

### 5.2 Veritabanı Tabloları

```
workflow_definitions:     id, name, version, entity_type, is_active, created_at
workflow_steps:           id, workflow_id, step_id, name, kind, config (JSON), sort_order, timeout_secs, timeout_action, retry_max, retry_delay_secs
workflow_transitions:     id, workflow_id, from_step_id, to_step_id, condition, label
workflow_instances:       id, workflow_name, workflow_version, entity_type, entity_id, tenant_id, current_step, status, context_data (JSON), started_by, started_at, completed_at
step_executions:          id, workflow_instance_id, step_id, status, assigned_to, result (JSON), error, attempt, started_at, completed_at, due_at
approval_records:         id, step_execution_id, approver_id, decision, comment, delegated_to, decided_at
```

### 5.3 Step Kind'lar ve Config Yapıları

```
action:       { "action_key": "po.validate" }
approval:     { "role": "manager", "min_approvals": 1, "any_can_reject": true }
gateway:      { "condition_key": "po.amount_check" }
parallel:     { "branches": [["step_a", "step_b"], ["step_c"]], "join": "all" }
wait_event:   { "event_type": "goods_received" }
timer:        { "duration_secs": 86400 }
sub_workflow: { "workflow": "invoice_approval" }
```

### 5.4 ActionRegistry

```csharp
public class ActionRegistry
{
    private readonly Dictionary<string, Func<ActionContext, Task<object?>>> _actions = new();
    private readonly Dictionary<string, Func<ActionContext, Task<string>>> _conditions = new();

    public void RegisterAction(string key, Func<ActionContext, Task<object?>> action);
    public void RegisterCondition(string key, Func<ActionContext, Task<string>> condition);

    // Bulk register — bir sınıftaki tüm [WorkflowAction] metodlarını otomatik kaydet
    public void Register<T>() where T : class;

    public Func<ActionContext, Task<object?>> GetAction(string key);
    public Func<ActionContext, Task<string>> GetCondition(string key);

    public IReadOnlyList<string> ListActions();
    public IReadOnlyList<string> ListConditions();
}

public class ActionContext
{
    public IServiceProvider Services { get; init; }
    public WorkflowInstance Instance { get; init; }
    public StepDefinition Step { get; init; }
    public ICurrentUser? CurrentUser { get; init; }
    public JsonElement Config { get; init; }

    // Convenience
    public T GetService<T>() => Services.GetRequiredService<T>();
    public IRepo<T> Repo<T>() where T : class, IEntity => GetService<IRepo<T>>();
}
```

### 5.5 WorkflowEngine

```csharp
public class WorkflowEngine
{
    // Workflow başlat
    Task<WorkflowInstance> Start(string workflowName, string entityType, string entityId, string tenantId, string startedBy, object? contextData = null);

    // Tip güvenli başlatma
    Task<WorkflowInstance> StartFor<T>(T entity, ICurrentUser user, object? contextData = null) where T : class, IEntity;

    // Onay/ret
    Task Approve(string instanceId, string stepId, ICurrentUser approver, string? comment = null);
    Task Reject(string instanceId, string stepId, ICurrentUser approver, string? comment = null);

    // Dış event gönder
    Task SendEvent(string instanceId, string eventType, object? payload = null);

    // Durum sorgula
    Task<WorkflowInstance> GetInstance(string instanceId);
    Task<WorkflowInstance?> GetActiveInstance(string entityType, string entityId);
    Task<bool> HasActiveInstance(string entityType, string entityId);
    Task<List<StepExecution>> GetHistory(string instanceId);

    // İptal
    Task Cancel(string instanceId, string cancelledBy, string? reason = null);

    // Cache
    Task InvalidateCache(string workflowName);
}
```

### 5.6 TimeoutChecker

```csharp
// BackgroundService olarak çalışır.
// Her 60 saniyede bir overdue step'leri kontrol eder.
// TimeoutAction'a göre: Escalate, AutoApprove, AutoReject, Notify, Fail.
// Fail durumunda kompanzasyon zinciri çalıştırılır.
```

### 5.7 Startup Validation

```csharp
// Uygulama başlangıcında:
// 1. Tüm workflow tanımlarını DB'den çek
// 2. Her step'in action_key'inin ActionRegistry'de kayıtlı olduğunu doğrula
// 3. Her step'in condition_key'inin (gateway) ActionRegistry'de kayıtlı olduğunu doğrula
// 4. Her transition'ın geçerli step_id'lere işaret ettiğini doğrula
// 5. Başarısızsa uygulamayı hata mesajı ile durdur
```

### 5.8 Config-Driven Action Örneği

```csharp
// Aynı action farklı config ile farklı davranış sergiler.
// DB'deki step config'i action'ın davranışını belirler.

[WorkflowAction("validate.required_fields")]
public async Task<object?> ValidateRequiredFields(ActionContext ctx)
{
    var fields = ctx.Config.GetProperty("fields").EnumerateArray()
        .Select(f => f.GetString()!).ToList();

    var entity = await ctx.Repo<dynamic>()...  // entity'yi getir
    var errors = new ValidationErrors();

    foreach (var field in fields)
    {
        // property değerini reflection ile oku
        // null veya boş ise hata ekle
    }

    errors.ThrowIfInvalid();
    return null;
}

// Şirket A config: { "fields": ["supplier_id", "total", "department_id"] }
// Şirket B config: { "fields": ["supplier_id", "total"] }
// Aynı action, farklı davranış
```

---

