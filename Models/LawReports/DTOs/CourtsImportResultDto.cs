public class CourtsImportResultDto
{
    public int CountryId { get; set; }
    public string Mode { get; set; } = "upsert";

    public int TotalRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }

    public List<CourtsImportErrorDto> Errors { get; set; } = new();
}

public class CourtsImportErrorDto
{
    public int Row { get; set; }               // 1-based row in CSV (excluding header)
    public string Message { get; set; } = "";
    public string? Name { get; set; }
    public string? Code { get; set; }
}
