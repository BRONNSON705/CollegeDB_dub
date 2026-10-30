
using System.Net.Mail;
using System.Data;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

const string connectionString =
    "Server=(localdb)\\MSSQLLocalDB;Database=CollegeDB;Integrated Security=True;TrustServerCertificate=True;";

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();

// Расчёт возраста по дате рождения
static int? CalculateAge(DateTime? birthDate)
{
    if (!birthDate.HasValue)
        return null;

    var today = DateTime.Today;
    var age = today.Year - birthDate.Value.Year;

    if (birthDate.Value.Date > today.AddYears(-age))
        age--;

    return age;
}

// Получить всех студентов
app.MapGet("/api/students", async () =>
{
    var students = new List<object>();

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT s.Id, s.FullName, s.Email, s.GroupId,
               g.Name AS GroupName, s.BirthDate
        FROM dbo.Students s
        LEFT JOIN dbo.Groups g ON s.GroupId = g.Id
        ORDER BY s.Id;
        """;

    await using var command = new SqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        DateTime? birthDate = reader.IsDBNull(5)
            ? null
            : reader.GetDateTime(5);

        students.Add(new
        {
            Id = reader.GetInt32(0),
            FullName = reader.GetString(1),
            Email = reader.GetString(2),
            GroupId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
            GroupName = reader.IsDBNull(4) ? null : reader.GetString(4),
            BirthDate = birthDate,
            Age = CalculateAge(birthDate)
        });
    }

    return Results.Ok(students);
});

// Получить одного студента
app.MapGet("/api/students/{id:int}", async (int id) =>
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT s.Id, s.FullName, s.Email, s.GroupId,
               g.Name AS GroupName, s.BirthDate
        FROM dbo.Students s
        LEFT JOIN dbo.Groups g ON s.GroupId = g.Id
        WHERE s.Id = @Id;
        """;

    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
        return Results.NotFound(new { message = "Студент не найден." });

    DateTime? birthDate = reader.IsDBNull(5)
        ? null
        : reader.GetDateTime(5);

    var student = new
    {
        Id = reader.GetInt32(0),
        FullName = reader.GetString(1),
        Email = reader.GetString(2),
        GroupId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
        GroupName = reader.IsDBNull(4) ? null : reader.GetString(4),
        BirthDate = birthDate,
        Age = CalculateAge(birthDate)
    };

    return Results.Ok(student);
});

// Добавить студента
app.MapPost("/api/students", async (StudentRequest request) =>
{
    var fullName = request.FullName?.Trim();
    var email = request.Email?.Trim();

    if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 100)
        return Results.BadRequest(new { message = "Укажите ФИО длиной до 100 символов." });

    if (string.IsNullOrWhiteSpace(email) || email.Length > 100)
        return Results.BadRequest(new { message = "Укажите email длиной до 100 символов." });

    try
    {
        _ = new MailAddress(email);
    }
    catch
    {
        return Results.BadRequest(new { message = "Укажите корректный email." });
    }

    if (request.BirthDate.HasValue &&
        request.BirthDate.Value.Date > DateTime.Today)
        return Results.BadRequest(new { message = "Дата рождения не может быть в будущем." });

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    if (request.GroupId.HasValue)
    {
        await using var check = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.Groups WHERE Id = @Id;", connection);

        check.Parameters.Add("@Id", SqlDbType.Int).Value = request.GroupId.Value;

        if ((int)await check.ExecuteScalarAsync()! == 0)
            return Results.BadRequest(new { message = "Указанная группа не найдена." });
    }

    const string sql = """
        INSERT INTO dbo.Students (FullName, Email, GroupId, BirthDate)
        OUTPUT INSERTED.Id
        VALUES (@FullName, @Email, @GroupId, @BirthDate);
        """;

    await using var command = new SqlCommand(sql, connection);

    command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = fullName;
    command.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = email;
    command.Parameters.Add("@GroupId", SqlDbType.Int).Value =
        (object?)request.GroupId ?? DBNull.Value;
    command.Parameters.Add("@BirthDate", SqlDbType.Date).Value =
        (object?)request.BirthDate?.Date ?? DBNull.Value;

    var newId = (int)(await command.ExecuteScalarAsync())!;

    return Results.Created($"/api/students/{newId}", new
    {
        Id = newId,
        FullName = fullName,
        Email = email,
        GroupId = request.GroupId,
        BirthDate = request.BirthDate?.Date,
        Age = CalculateAge(request.BirthDate)
    });
});

// Изменить студента
app.MapPut("/api/students/{id:int}", async (int id, StudentRequest request) =>
{
    var fullName = request.FullName?.Trim();
    var email = request.Email?.Trim();

    if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 100)
        return Results.BadRequest(new { message = "Укажите ФИО длиной до 100 символов." });

    if (string.IsNullOrWhiteSpace(email) || email.Length > 100)
        return Results.BadRequest(new { message = "Укажите email длиной до 100 символов." });

    try
    {
        _ = new MailAddress(email);
    }
    catch
    {
        return Results.BadRequest(new { message = "Укажите корректный email." });
    }

    if (request.BirthDate.HasValue &&
        request.BirthDate.Value.Date > DateTime.Today)
        return Results.BadRequest(new { message = "Дата рождения не может быть в будущем." });

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    if (request.GroupId.HasValue)
    {
        await using var check = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.Groups WHERE Id = @Id;", connection);

        check.Parameters.Add("@Id", SqlDbType.Int).Value = request.GroupId.Value;

        if ((int)await check.ExecuteScalarAsync()! == 0)
            return Results.BadRequest(new { message = "Указанная группа не найдена." });
    }

    const string sql = """
        UPDATE dbo.Students
        SET FullName = @FullName,
            Email = @Email,
            GroupId = @GroupId,
            BirthDate = @BirthDate
        WHERE Id = @Id;
        """;

    await using var command = new SqlCommand(sql, connection);

    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
    command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = fullName;
    command.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = email;
    command.Parameters.Add("@GroupId", SqlDbType.Int).Value =
        (object?)request.GroupId ?? DBNull.Value;
    command.Parameters.Add("@BirthDate", SqlDbType.Date).Value =
        (object?)request.BirthDate?.Date ?? DBNull.Value;

    if (await command.ExecuteNonQueryAsync() == 0)
        return Results.NotFound(new { message = "Студент не найден." });

    return Results.Ok(new
    {
        Id = id,
        FullName = fullName,
        Email = email,
        GroupId = request.GroupId,
        BirthDate = request.BirthDate?.Date,
        Age = CalculateAge(request.BirthDate)
    });
});

// Удалить студента
app.MapDelete("/api/students/{id:int}", async (int id) =>
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new SqlCommand(
        "DELETE FROM dbo.Students WHERE Id = @Id;", connection);

    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

    if (await command.ExecuteNonQueryAsync() == 0)
        return Results.NotFound(new { message = "Студент не найден." });

    return Results.Ok(new { message = "Студент удалён." });
});

// Получить группы и количество студентов
app.MapGet("/api/groups", async () =>
{
    var groups = new List<object>();

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT g.Id, g.Name, COUNT(s.Id) AS StudentCount
        FROM dbo.Groups g
        LEFT JOIN dbo.Students s ON s.GroupId = g.Id
        GROUP BY g.Id, g.Name
        ORDER BY g.Id;
        """;

    await using var command = new SqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        groups.Add(new
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            StudentCount = reader.GetInt32(2)
        });
    }

    return Results.Ok(groups);
});

// Добавить группу
app.MapPost("/api/groups", async (GroupRequest request) =>
{
    var name = request.Name?.Trim();

    if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
        return Results.BadRequest(new { message = "Укажите название группы длиной до 50 символов." });

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        INSERT INTO dbo.Groups (Name)
        OUTPUT INSERTED.Id
        VALUES (@Name);
        """;

    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@Name", SqlDbType.NVarChar, 50).Value = name;

    try
    {
        var newId = (int)(await command.ExecuteScalarAsync())!;
        return Results.Created($"/api/groups/{newId}", new { Id = newId, Name = name });
    }
    catch (SqlException ex) when (ex.Number is 2601 or 2627)
    {
        return Results.Conflict(new { message = "Такая группа уже существует." });
    }
});

// Удалить группу, если в ней нет студентов
app.MapDelete("/api/groups/{id:int}", async (int id) =>
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var check = new SqlCommand(
        "SELECT COUNT(*) FROM dbo.Students WHERE GroupId = @Id;", connection);

    check.Parameters.Add("@Id", SqlDbType.Int).Value = id;

    if ((int)await check.ExecuteScalarAsync()! > 0)
        return Results.Conflict(new { message = "Нельзя удалить группу, в которой есть студенты." });

    await using var command = new SqlCommand(
        "DELETE FROM dbo.Groups WHERE Id = @Id;", connection);

    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

    if (await command.ExecuteNonQueryAsync() == 0)
        return Results.NotFound(new { message = "Группа не найдена." });

    return Results.Ok(new { message = "Группа удалена." });
});

app.Run();

public sealed record StudentRequest(
    string? FullName,
    string? Email,
    int? GroupId,
    DateTime? BirthDate
);

public sealed record GroupRequest(string? Name);
