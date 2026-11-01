namespace DubUrl.Schema.QA;

[Flags]
public enum SchemaCapabilities
{
    None = 0,
    CreateTable = 1,
    DropTableIfExists = 2,
}
