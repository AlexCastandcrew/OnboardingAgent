using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnboardingAgent.Core.Ingestion;

namespace OnboardingAgent.Core.Retrieval;

public sealed record IndexedDocument(
    string DocumentId,
    string ProjectId,
    string Title,
    string Path,
    string SourceType,
    string? SourceUrl,
    string? SourceVersion,
    string? SourceUpdatedAt,
    string IngestedAt,
    string Checksum,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Initiatives,
    string Content);

public sealed record IndexHit(
    string ChunkId,
    string DocumentId,
    string Title,
    string Section,
    string Text,
    double Bm25Score,
    string SourceType,
    string? SourceUrl,
    string? SourceVersion,
    string? SourceUpdatedAt,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Initiatives);

/// <summary>SQLite store for documents and chunks, with an FTS5 table for BM25 search.</summary>
public sealed class KnowledgeIndex
{
    private readonly string _connectionString;

    public KnowledgeIndex(string databasePath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = true }.ToString();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS documents (
                document_id       TEXT PRIMARY KEY,
                project_id        TEXT NOT NULL,
                title             TEXT NOT NULL,
                path              TEXT NOT NULL,
                source_type       TEXT NOT NULL,
                source_url        TEXT,
                source_version    TEXT,
                source_updated_at TEXT,
                ingested_at       TEXT NOT NULL,
                checksum          TEXT NOT NULL,
                domains           TEXT NOT NULL,
                initiatives       TEXT NOT NULL,
                content           TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chunks (
                chunk_id    TEXT PRIMARY KEY,
                document_id TEXT NOT NULL,
                ordinal     INTEGER NOT NULL,
                section     TEXT NOT NULL,
                text        TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_chunks_document ON chunks(document_id);
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
                chunk_id UNINDEXED, title, section, text,
                tokenize = 'unicode61 remove_diacritics 2'
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Every project that has at least one indexed document.</summary>
    public IReadOnlyList<string> GetProjectIds()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT project_id FROM documents";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public Dictionary<string, string> GetChecksums(string projectId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_id, checksum FROM documents WHERE project_id = $p";
        command.Parameters.AddWithValue("$p", projectId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    /// <summary>Replaces the document and all of its chunks in one transaction.</summary>
    public void Upsert(IndexedDocument document, IReadOnlyList<Chunk> chunks)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        DeleteDocument(connection, transaction, document.DocumentId);

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO documents (document_id, project_id, title, path, source_type, source_url, source_version,
                                       source_updated_at, ingested_at, checksum, domains, initiatives, content)
                VALUES ($id, $project, $title, $path, $type, $url, $version, $updated, $ingested, $checksum,
                        $domains, $initiatives, $content)
                """;
            insert.Parameters.AddWithValue("$id", document.DocumentId);
            insert.Parameters.AddWithValue("$project", document.ProjectId);
            insert.Parameters.AddWithValue("$title", document.Title);
            insert.Parameters.AddWithValue("$path", document.Path);
            insert.Parameters.AddWithValue("$type", document.SourceType);
            insert.Parameters.AddWithValue("$url", (object?)document.SourceUrl ?? DBNull.Value);
            insert.Parameters.AddWithValue("$version", (object?)document.SourceVersion ?? DBNull.Value);
            insert.Parameters.AddWithValue("$updated", (object?)document.SourceUpdatedAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("$ingested", document.IngestedAt);
            insert.Parameters.AddWithValue("$checksum", document.Checksum);
            insert.Parameters.AddWithValue("$domains", JsonSerializer.Serialize(document.Domains));
            insert.Parameters.AddWithValue("$initiatives", JsonSerializer.Serialize(document.Initiatives));
            insert.Parameters.AddWithValue("$content", document.Content);
            insert.ExecuteNonQuery();
        }

        using var chunk = connection.CreateCommand();
        chunk.Transaction = transaction;
        chunk.CommandText = """
            INSERT INTO chunks (chunk_id, document_id, ordinal, section, text) VALUES ($id, $doc, $ordinal, $section, $text);
            INSERT INTO chunks_fts (chunk_id, title, section, text) VALUES ($id, $title, $section, $text);
            """;
        var id = chunk.Parameters.Add("$id", SqliteType.Text);
        var ordinal = chunk.Parameters.Add("$ordinal", SqliteType.Integer);
        var section = chunk.Parameters.Add("$section", SqliteType.Text);
        var text = chunk.Parameters.Add("$text", SqliteType.Text);
        chunk.Parameters.AddWithValue("$doc", document.DocumentId);
        chunk.Parameters.AddWithValue("$title", document.Title);
        foreach (var c in chunks)
        {
            id.Value = c.ChunkId;
            ordinal.Value = c.Ordinal;
            section.Value = c.Section;
            text.Value = c.Text;
            chunk.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Remove(string documentId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        DeleteDocument(connection, transaction, documentId);
        transaction.Commit();
    }

    private static void DeleteDocument(SqliteConnection connection, SqliteTransaction transaction, string documentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM chunks_fts WHERE chunk_id IN (SELECT chunk_id FROM chunks WHERE document_id = $id);
            DELETE FROM chunks WHERE document_id = $id;
            DELETE FROM documents WHERE document_id = $id;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        command.ExecuteNonQuery();
    }

    /// <summary>Runs an FTS5 MATCH query. Higher <see cref="IndexHit.Bm25Score"/> is better.</summary>
    public IReadOnlyList<IndexHit> Search(string projectId, string matchQuery, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // bm25() is lower-is-better; weights: chunk_id 0, title 3, section 2, text 1.
        command.CommandText = """
            SELECT c.chunk_id, c.document_id, d.title, c.section, c.text,
                   -bm25(chunks_fts, 0.0, 3.0, 2.0, 1.0) AS score,
                   d.source_type, d.source_url, d.source_version, d.source_updated_at, d.domains, d.initiatives
            FROM chunks_fts f
            JOIN chunks c ON c.chunk_id = f.chunk_id
            JOIN documents d ON d.document_id = c.document_id
            WHERE chunks_fts MATCH $q AND d.project_id = $p
            ORDER BY score DESC
            LIMIT $n
            """;
        command.Parameters.AddWithValue("$q", matchQuery);
        command.Parameters.AddWithValue("$p", projectId);
        command.Parameters.AddWithValue("$n", limit);

        var hits = new List<IndexHit>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(new IndexHit(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                NullableString(reader, 7),
                NullableString(reader, 8),
                NullableString(reader, 9),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(10)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(11)) ?? []));
        }

        return hits;
    }

    public IndexedDocument? GetDocument(string documentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_id, project_id, title, path, source_type, source_url, source_version, source_updated_at,
                   ingested_at, checksum, domains, initiatives, content
            FROM documents WHERE document_id = $id
            """;
        command.Parameters.AddWithValue("$id", documentId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDocument(reader) : null;
    }

    /// <summary>Document id → title, source type and url, for the onboarding steps.</summary>
    public IReadOnlyList<IndexedDocument> GetDocuments(string projectId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_id, project_id, title, path, source_type, source_url, source_version, source_updated_at,
                   ingested_at, checksum, domains, initiatives, ''
            FROM documents WHERE project_id = $p
            """;
        command.Parameters.AddWithValue("$p", projectId);
        using var reader = command.ExecuteReader();
        var result = new List<IndexedDocument>();
        while (reader.Read())
        {
            result.Add(ReadDocument(reader));
        }

        return result;
    }

    public IReadOnlyList<(string ChunkId, string Section)> GetSections(string documentId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT chunk_id, section FROM chunks WHERE document_id = $id ORDER BY ordinal";
        command.Parameters.AddWithValue("$id", documentId);
        using var reader = command.ExecuteReader();
        var result = new List<(string, string)>();
        while (reader.Read())
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    public (int Documents, int Chunks, string? LastIngestedAt) Stats()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM documents), (SELECT COUNT(*) FROM chunks), (SELECT MAX(ingested_at) FROM documents)
            """;
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt32(1), NullableString(reader, 2));
    }

    private static IndexedDocument ReadDocument(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        NullableString(reader, 5),
        NullableString(reader, 6),
        NullableString(reader, 7),
        reader.GetString(8),
        reader.GetString(9),
        JsonSerializer.Deserialize<List<string>>(reader.GetString(10)) ?? [],
        JsonSerializer.Deserialize<List<string>>(reader.GetString(11)) ?? [],
        reader.GetString(12));

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
