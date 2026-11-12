using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PixView.Core.Books;

/// <summary>
/// 本の情報を保存する保管庫。保存先は、SQLite のデータベースのファイル。
/// </summary>
/// <remarks>
/// <para>
/// 各メソッドは、データベースの処理をスレッド プールで行う。
/// Microsoft.Data.Sqlite の非同期メソッドは、内部で同期的に動くため、画面のスレッドから呼んでも止まらないように、ここで退避させている。
/// </para>
/// <para>
/// 引数の <see cref="CancellationToken"/> は、処理を始める前にだけ確認する。始まった処理は、最後まで行う。
/// </para>
/// <para>
/// 複数のスレッドから同時に呼び出してよい。
/// </para>
/// </remarks>
public sealed class BookRepository : IDisposable
{
    private const string Columns = "id, title, author, source_location, source_kind, added_at, last_read_at";

    private const string InsertCommandText = $"""
        INSERT INTO books ({Columns})
        VALUES ($id, $title, $author, $source_location, $source_kind, $added_at, $last_read_at)
        ON CONFLICT (source_location) DO NOTHING;
        """;

    private const string SelectByIdCommandText = $"SELECT {Columns} FROM books WHERE id = $id;";

    private const string SelectBySourceLocationCommandText = $"SELECT {Columns} FROM books WHERE source_location = $source_location;";

    private const string SelectAllCommandText = $"SELECT {Columns} FROM books;";

    private const string UpdateCommandText = """
        UPDATE books
        SET title = $title, author = $author, last_read_at = $last_read_at
        WHERE id = $id;
        """;

    private const string DeleteCommandText = "DELETE FROM books WHERE id = $id;";

    // 日時は、端末のタイムゾーンに左右されないように、UTC の ISO 8601 形式の文字列（例：2026-11-12T00:30:15.123Z）で記録する。
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private volatile bool _disposed;

    private BookRepository(string connectionString, TimeProvider timeProvider)
    {
        _connectionString = connectionString;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// 保管庫を開く。データベースのファイルがなければ作る。
    /// </summary>
    /// <param name="databasePath">データベースのファイルの場所。ファイルを置くフォルダは、あらかじめ作っておく。</param>
    /// <param name="timeProvider">本を追加した日時を得るために使う。</param>
    /// <param name="cancellationToken">処理を取り消すためのトークン。</param>
    /// <returns>開いた保管庫。</returns>
    /// <exception cref="NotSupportedException">
    /// データベースが、このアプリより新しい版の定義になっている。
    /// </exception>
    /// <exception cref="SqliteException">
    /// ファイルを開けない。ファイルが SQLite のデータベースでない場合や、このアプリが作ったものでない場合を含む。
    /// </exception>
    public static Task<BookRepository> OpenAsync(
        string databasePath,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // 接続を使い回す。Dispose で、使い回し用に残っている接続を閉じる。
            Pooling = true,
        }.ToString();

        return Task.Run(() => Open(connectionString, timeProvider), cancellationToken);
    }

    /// <summary>
    /// 本を追加する。
    /// </summary>
    /// <param name="title">題名。</param>
    /// <param name="author">作者。設定しない場合は <see langword="null"/>。空の文字列は渡せない。</param>
    /// <param name="sourceLocation">元になるファイルの場所。</param>
    /// <param name="sourceKind">元になるファイルの種類。</param>
    /// <param name="cancellationToken">処理を取り消すためのトークン。</param>
    /// <returns>
    /// 追加した結果。同じ場所の本がすでに登録されている場合は、新しい本を作らずに、登録済みの本を返す。
    /// 場所が同じかどうかは、文字列の完全一致で判定する。
    /// </returns>
    public Task<AddBookResult> AddAsync(
        string title,
        string? author,
        string sourceLocation,
        BookSourceKind sourceKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (author is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(author);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocation);
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null);
        }

        return RunAsync(
            connection => Add(connection, title, author, sourceLocation, sourceKind),
            cancellationToken);
    }

    /// <summary>
    /// 識別子を指定して、本を取得する。
    /// </summary>
    /// <param name="id">本の識別子。</param>
    /// <param name="cancellationToken">処理を取り消すためのトークン。</param>
    /// <returns>本。見つからなければ <see langword="null"/>。</returns>
    public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return RunAsync(connection => Get(connection, id), cancellationToken);
    }

    /// <summary>
    /// 登録されているすべての本を取得する。
    /// </summary>
    /// <param name="cancellationToken">処理を取り消すためのトークン。</param>
    /// <returns>本の一覧。順序は決めていない。</returns>
    public Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(List, cancellationToken);
    }

    /// <summary>
    /// 本の題名、作者、最後に読んだ日時を更新する。
    /// </summary>
    /// <param name="book">
    /// 更新後の本。<see cref="Book.Id"/> で更新する本を決める。
    /// 元になるファイルの場所と種類、追加した日時は、更新しない。
    /// </param>
    /// <param name="cancellationToken">処理を取り消すためのトークン。</param>
    /// <returns>更新した場合は <see langword="true"/>。本が見つからなかった場合は <see langword="false"/>。</returns>
    public Task<bool> UpdateAsync(Book book, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrWhiteSpace(book.Title);
        if (book.Author is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(book.Author);
        }

        return RunAsync(connection => Update(connection, book), cancellationToken);
    }

    /// <summary>
    /// 本を削除する。元になるファイルは削除しない。
    /// </summary>
    /// <param name="id">本の識別子。</param>
    /// <param name="cancellationToken">処理を取り消すためのトークン。</param>
    /// <returns>削除した場合は <see langword="true"/>。本が見つからなかった場合は <see langword="false"/>。</returns>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return RunAsync(connection => Delete(connection, id), cancellationToken);
    }

    /// <summary>
    /// 保管庫を閉じる。使い回し用に残っている、データベースへの接続を閉じる。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearPool();
    }

    private void ClearPool()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }

    private static BookRepository Open(string connectionString, TimeProvider timeProvider)
    {
        // try / catch で Dispose してから例外を再送出する形では、CA2000 の指摘が消えないため、この形にしている。
        BookRepository? repository = new BookRepository(connectionString, timeProvider);
        try
        {
            using (var connection = repository.OpenConnection())
            {
                BookDatabaseSchema.Initialize(connection);
            }

            var opened = repository;
            repository = null;
            return opened;
        }
        finally
        {
            // 初期化に失敗したときに、使い回し用に残った接続が、ファイルを開いたままにしないようにする。
            repository?.Dispose();
        }
    }

    private AddBookResult Add(
        SqliteConnection connection,
        string title,
        string? author,
        string sourceLocation,
        BookSourceKind sourceKind)
    {
        var addedAt = TruncateToMilliseconds(_timeProvider.GetUtcNow());
        var book = new Book
        {
            // 時刻順に並ぶ GUID にして、索引に追加するときの効率を上げる。
            Id = Guid.CreateVersion7(addedAt),
            Title = title,
            Author = author,
            SourceLocation = sourceLocation,
            SourceKind = sourceKind,
            AddedAt = addedAt,
            LastReadAt = null,
        };

        // 書き込みのロックを最初に取り、追加と、登録済みの本の読み出しの間に、別の処理が割り込まないようにする。
        using var transaction = connection.BeginTransaction(deferred: false);

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = InsertCommandText;
            insert.Parameters.AddWithValue("$id", book.Id);
            insert.Parameters.AddWithValue("$title", book.Title);
            insert.Parameters.AddWithValue("$author", ToDbValue(book.Author));
            insert.Parameters.AddWithValue("$source_location", book.SourceLocation);
            insert.Parameters.AddWithValue("$source_kind", (int)book.SourceKind);
            insert.Parameters.AddWithValue("$added_at", ToDbValue(book.AddedAt));
            insert.Parameters.AddWithValue("$last_read_at", ToDbValue(book.LastReadAt));

            if (insert.ExecuteNonQuery() == 1)
            {
                transaction.Commit();
                return new AddBookResult(book, IsAlreadyRegistered: false);
            }
        }

        using var select = connection.CreateCommand();
        select.CommandText = SelectBySourceLocationCommandText;
        select.Parameters.AddWithValue("$source_location", sourceLocation);

        // 追加できなかったのは、同じ場所の本があったときだけ。同じトランザクションの中なので、必ず見つかる。
        var existing = ReadSingle(select) ?? throw new UnreachableException();

        transaction.Commit();
        return new AddBookResult(existing, IsAlreadyRegistered: true);
    }

    private static Book? Get(SqliteConnection connection, Guid id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectByIdCommandText;
        command.Parameters.AddWithValue("$id", id);
        return ReadSingle(command);
    }

    private static IReadOnlyList<Book> List(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectAllCommandText;

        using var reader = command.ExecuteReader();
        var books = new List<Book>();
        while (reader.Read())
        {
            books.Add(ReadBook(reader));
        }

        return books;
    }

    private static bool Update(SqliteConnection connection, Book book)
    {
        using var command = connection.CreateCommand();
        command.CommandText = UpdateCommandText;
        command.Parameters.AddWithValue("$id", book.Id);
        command.Parameters.AddWithValue("$title", book.Title);
        command.Parameters.AddWithValue("$author", ToDbValue(book.Author));
        command.Parameters.AddWithValue("$last_read_at", ToDbValue(book.LastReadAt));
        return command.ExecuteNonQuery() == 1;
    }

    private static bool Delete(SqliteConnection connection, Guid id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = DeleteCommandText;
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 1;
    }

    private static Book? ReadSingle(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBook(reader) : null;
    }

    // 列の順序は、Columns に合わせる。
    private static Book ReadBook(SqliteDataReader reader)
    {
        return new Book
        {
            Id = reader.GetGuid(0),
            Title = reader.GetString(1),
            Author = reader.IsDBNull(2) ? null : reader.GetString(2),
            SourceLocation = reader.GetString(3),
            SourceKind = ToSourceKind(reader.GetInt64(4)),
            AddedAt = ToDateTimeOffset(reader.GetString(5)),
            LastReadAt = reader.IsDBNull(6) ? null : ToDateTimeOffset(reader.GetString(6)),
        };
    }

    private static object ToDbValue(string? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private static object ToDbValue(DateTimeOffset? value)
    {
        return value is { } v ? v.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture) : DBNull.Value;
    }

    private static DateTimeOffset ToDateTimeOffset(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"保存先に、日時として読めない値（{value}）が記録されています。"));
        }

        return result;
    }

    // 保存できるのはミリ秒までなので、返す値を、保存する値とそろえる。
    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static BookSourceKind ToSourceKind(long value)
    {
        // int の範囲外の値をそのままキャストすると、上位のビットが落ちて、定義済みの値に化けることがある。
        if (value is < int.MinValue or > int.MaxValue || !Enum.IsDefined((BookSourceKind)value))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"保存先に、未知の種類の値（{value}）が記録されています。"));
        }

        return (BookSourceKind)value;
    }

    private Task<T> RunAsync<T>(Func<SqliteConnection, T> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                try
                {
                    using var connection = OpenConnection();
                    return operation(connection);
                }
                finally
                {
                    // 処理の途中で Dispose されると、その後に閉じた接続が、使い回し用に残ってしまう。
                    // ここで改めて片付けて、Dispose の後にファイルが開いたままにならないようにする。
                    if (_disposed)
                    {
                        ClearPool();
                    }
                }
            },
            cancellationToken);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
