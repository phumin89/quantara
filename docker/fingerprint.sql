SET NOCOUNT ON;
DECLARE @manifest TABLE ([Table] nvarchar(517), [Rows] bigint, [SchemaHash] varchar(64), [DataHash] varchar(64));
DECLARE @schema sysname, @table sysname, @object int, @query nvarchar(max), @columns nvarchar(max), @name nvarchar(517);
DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name, t.name, t.object_id FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name, t.name;
OPEN tables;
FETCH NEXT FROM tables INTO @schema, @table, @object;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @columns=(SELECT c.name, c.column_id, ty.name AS [type], c.max_length, c.precision, c.scale,
        c.is_nullable, c.is_identity, c.collation_name, dc.definition AS [default], cc.definition AS computed
        FROM sys.columns c JOIN sys.types ty ON c.user_type_id=ty.user_type_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
        WHERE c.object_id=@object ORDER BY c.column_id FOR JSON PATH, INCLUDE_NULL_VALUES);
    SET @query=N'SELECT @name,COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES(''SHA2_256'',@columns),2),'
        +N'CONVERT(varchar(64),HASHBYTES(''SHA2_256'',COALESCE(STRING_AGG(CONVERT(varchar(max),RowHash),'''')'
        +N' WITHIN GROUP (ORDER BY RowHash),'''')),2) FROM (SELECT CONVERT(varchar(64),'
        +N'HASHBYTES(''SHA2_256'',(SELECT t.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)),2) RowHash FROM '
        +QUOTENAME(@schema)+N'.'+QUOTENAME(@table)+N' t) rows;';
    SET @name=QUOTENAME(@schema)+N'.'+QUOTENAME(@table);
    INSERT @manifest EXEC sp_executesql @query,N'@name nvarchar(517),@columns nvarchar(max)',
        @name=@name,@columns=@columns;
    FETCH NEXT FROM tables INTO @schema, @table, @object;
END;
CLOSE tables;
DEALLOCATE tables;
SELECT * FROM @manifest ORDER BY [Table] FOR JSON PATH;
