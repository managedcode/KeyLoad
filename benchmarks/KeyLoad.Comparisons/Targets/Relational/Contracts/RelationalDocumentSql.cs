using System.Data.Common;

namespace KeyLoad.Comparisons.Targets;

internal sealed record RelationalDocumentSql(string Name)
{
    private const string Extension = "CREATE EXTENSION IF NOT EXISTS timescaledb";
    private const string SqlCreate = "DECLARE @ddl nvarchar(max)=N'CREATE DATABASE '+QUOTENAME(@namespace); EXEC sys.sp_executesql @ddl;";
    private const string SqlDrop = "DECLARE @ddl nvarchar(max)=N'DROP DATABASE '+QUOTENAME(@namespace); EXEC sys.sp_executesql @ddl;";
    private const string MySqlCreate = "SET @ddl=CONCAT('CREATE DATABASE `',REPLACE(@namespace,'`','``'),'`'); PREPARE ddl_statement FROM @ddl; EXECUTE ddl_statement; DEALLOCATE PREPARE ddl_statement;";
    private const string MySqlDrop = "SET @ddl=CONCAT('DROP DATABASE `',REPLACE(@namespace,'`','``'),'`'); PREPARE ddl_statement FROM @ddl; EXECUTE ddl_statement; DEALLOCATE PREPARE ddl_statement;";
    private const string PgCreate = "SELECT set_config('keyload.benchmark.namespace',@namespace,false); DO $$ BEGIN EXECUTE format('CREATE SCHEMA %I',current_setting('keyload.benchmark.namespace')); END $$;";
    private const string PgDrop = "SELECT set_config('keyload.benchmark.namespace',@namespace,false); DO $$ BEGIN EXECUTE format('DROP SCHEMA %I CASCADE',current_setting('keyload.benchmark.namespace')); END $$;";
    private const string SqlTable = "CREATE TABLE documents(id nvarchar(64) COLLATE Latin1_General_100_BIN2 PRIMARY KEY,body nvarchar(1024) NOT NULL,number int NOT NULL)";
    private const string MySqlTable = "CREATE TABLE documents(id varchar(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,body text NOT NULL,number int NOT NULL) ENGINE=InnoDB";
    private const string PgTable = "CREATE TABLE documents(id text COLLATE \"C\" NOT NULL,body text NOT NULL,number integer NOT NULL,at timestamptz NOT NULL,PRIMARY KEY(id,at)); SELECT create_hypertable('documents','at',chunk_time_interval=>INTERVAL '1 day')";
    private const string SqlObserve = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),delayed_durability_desc FROM sys.databases WHERE name=DB_NAME()";
    private const string MySqlObserve = "SELECT VERSION(),CONCAT(@@innodb_flush_log_at_trx_commit,':',@@sync_binlog)";
    private const string PgObserve = "SELECT extversion,current_setting('fsync')||':'||current_setting('synchronous_commit') FROM pg_extension WHERE extname='timescaledb'";
    private const string Insert = "INSERT INTO documents(id,body,number) VALUES (@id,@body,@number)";
    private const string PgInsert = "INSERT INTO documents(id,body,number,at) VALUES (@id,@body,@number,TIMESTAMPTZ '2026-01-01' + @number * INTERVAL '1 microsecond')";
    private const string Update = "UPDATE documents SET body=@body WHERE id=@id";
    private const string Delete = "DELETE FROM documents WHERE id=@id";
    private const string SqlRead = "SELECT TOP (2) id,body FROM documents WHERE id=@id";
    private const string Read = "SELECT id,body FROM documents WHERE id=@id LIMIT 2";
    private const string SqlQuery = "SELECT TOP (2) id,body FROM documents WHERE id=@id AND number>=@number AND number<@number+1 ORDER BY id";
    private const string Query = "SELECT id,body FROM documents WHERE id=@id AND number>=@number AND number<@number+1 ORDER BY id LIMIT 2";
    private const string Enumerate = "SELECT id,body FROM documents ORDER BY id";
    private const string Index = "CREATE INDEX session_number_idx ON documents(number)";
    private const string SqlSetting = "DISABLED", MySqlSetting = "1:1", PgSetting = "on:on";
    private const string SqlAck = "autocommit; delayed durability disabled; one primary log flush";
    private const string MySqlAck = "InnoDB autocommit; innodb_flush_log_at_trx_commit=1; sync_binlog=1";
    private const string PgAck = "TimescaleDB hypertable; fsync=on; synchronous_commit=on; one primary WAL flush";
    internal string RequiredSetting => Name == BenchmarkAdditionalTargets.SqlServer ? SqlSetting : Name == BenchmarkAdditionalTargets.MySql ? MySqlSetting : PgSetting;
    internal string Acknowledgement => Name == BenchmarkAdditionalTargets.SqlServer ? SqlAck : Name == BenchmarkAdditionalTargets.MySql ? MySqlAck : PgAck;
    internal RelationalDocumentBatch Batch { get; } = new(Name);

    internal DbCommand Command(DbConnection connection, RelationalDocumentCommand operation, TimeSpan timeout)
    {
        var command = connection.CreateCommand();
        if (Name == BenchmarkAdditionalTargets.SqlServer)
        {
            BindSqlServer(command, operation);
        }
        else if (Name == BenchmarkAdditionalTargets.MySql)
        {
            BindMySql(command, operation);
        }
        else
        {
            BindTimescale(command, operation);
        }
        command.CommandTimeout = checked((int)Math.Ceiling(timeout.TotalSeconds));
        return command;
    }

    private static void BindSqlServer(DbCommand command, RelationalDocumentCommand operation)
    {
        switch (operation)
        {
            case RelationalDocumentCommand.InitializeExtension:
                command.CommandText = Extension;
                break;
            case RelationalDocumentCommand.CreateNamespace:
                command.CommandText = SqlCreate;
                break;
            case RelationalDocumentCommand.DropNamespace:
                command.CommandText = SqlDrop;
                break;
            case RelationalDocumentCommand.CreateTable:
                command.CommandText = SqlTable;
                break;
            case RelationalDocumentCommand.Observe:
                command.CommandText = SqlObserve;
                break;
            case RelationalDocumentCommand.Insert:
                command.CommandText = Insert;
                break;
            case RelationalDocumentCommand.Update:
                command.CommandText = Update;
                break;
            case RelationalDocumentCommand.Delete:
                command.CommandText = Delete;
                break;
            case RelationalDocumentCommand.Read:
                command.CommandText = SqlRead;
                break;
            case RelationalDocumentCommand.Enumerate:
                command.CommandText = Enumerate;
                break;
            case RelationalDocumentCommand.Index:
                command.CommandText = Index;
                break;
            case RelationalDocumentCommand.Query:
                command.CommandText = SqlQuery;
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private static void BindMySql(DbCommand command, RelationalDocumentCommand operation)
    {
        switch (operation)
        {
            case RelationalDocumentCommand.InitializeExtension:
                command.CommandText = Extension;
                break;
            case RelationalDocumentCommand.CreateNamespace:
                command.CommandText = MySqlCreate;
                break;
            case RelationalDocumentCommand.DropNamespace:
                command.CommandText = MySqlDrop;
                break;
            case RelationalDocumentCommand.CreateTable:
                command.CommandText = MySqlTable;
                break;
            case RelationalDocumentCommand.Observe:
                command.CommandText = MySqlObserve;
                break;
            case RelationalDocumentCommand.Insert:
                command.CommandText = Insert;
                break;
            case RelationalDocumentCommand.Update:
                command.CommandText = Update;
                break;
            case RelationalDocumentCommand.Delete:
                command.CommandText = Delete;
                break;
            case RelationalDocumentCommand.Read:
                command.CommandText = Read;
                break;
            case RelationalDocumentCommand.Enumerate:
                command.CommandText = Enumerate;
                break;
            case RelationalDocumentCommand.Index:
                command.CommandText = Index;
                break;
            case RelationalDocumentCommand.Query:
                command.CommandText = Query;
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private static void BindTimescale(DbCommand command, RelationalDocumentCommand operation)
    {
        switch (operation)
        {
            case RelationalDocumentCommand.InitializeExtension:
                command.CommandText = Extension;
                break;
            case RelationalDocumentCommand.CreateNamespace:
                command.CommandText = PgCreate;
                break;
            case RelationalDocumentCommand.DropNamespace:
                command.CommandText = PgDrop;
                break;
            case RelationalDocumentCommand.CreateTable:
                command.CommandText = PgTable;
                break;
            case RelationalDocumentCommand.Observe:
                command.CommandText = PgObserve;
                break;
            case RelationalDocumentCommand.Insert:
                command.CommandText = PgInsert;
                break;
            case RelationalDocumentCommand.Update:
                command.CommandText = Update;
                break;
            case RelationalDocumentCommand.Delete:
                command.CommandText = Delete;
                break;
            case RelationalDocumentCommand.Read:
                command.CommandText = Read;
                break;
            case RelationalDocumentCommand.Enumerate:
                command.CommandText = Enumerate;
                break;
            case RelationalDocumentCommand.Index:
                command.CommandText = Index;
                break;
            case RelationalDocumentCommand.Query:
                command.CommandText = Query;
                break;
            default:
                throw new NotSupportedException();
        }
    }
}
