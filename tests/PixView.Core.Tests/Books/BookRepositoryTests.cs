using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using PixView.Core.Books;

namespace PixView.Core.Tests.Books;

[TestClass]
public sealed class BookRepositoryTests : IDisposable
{
    // 保管庫は日時をミリ秒単位で保存するので、ミリ秒まで値を持たせる。
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 0, 30, 15, 123, TimeSpan.Zero);

    private const string FolderLocation = @"C:\Books\Folder";

    private const string ZipLocation = @"C:\Books\Archive.zip";

    private const string AndroidLocation = "content://com.android.externalstorage.documents/tree/primary%3ABooks%2FFolder";

    private readonly TestContext _testContext;
    private readonly DirectoryInfo _directory;
    private readonly string _databasePath;

    public BookRepositoryTests(TestContext testContext)
    {
        _testContext = testContext;
        _directory = Directory.CreateTempSubdirectory("PixView.Core.Tests.");
        _databasePath = Path.Combine(_directory.FullName, "books.db");
    }

    private CancellationToken CancellationToken => _testContext.CancellationToken;

    public void Dispose()
    {
        // 保管庫を閉じ忘れてファイルが開いたままだと、ここで削除に失敗する。
        _directory.Delete(recursive: true);
    }

    /// <summary>
    /// 追加した本が、渡した題名、作者、場所、種類と、追加した時点の日時を持ち、新しく作られた本として返ることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task AddAsync_NewLocation_ReturnsAddedBook()
    {
        using var repository = await OpenRepositoryAsync();

        var result = await repository.AddAsync("題名", "作者", FolderLocation, BookSourceKind.Folder, CancellationToken);

        Assert.IsFalse(result.IsAlreadyRegistered);
        Assert.AreNotEqual(Guid.Empty, result.Book.Id);
        Assert.AreEqual("題名", result.Book.Title);
        Assert.AreEqual("作者", result.Book.Author);
        Assert.AreEqual(FolderLocation, result.Book.SourceLocation);
        Assert.AreEqual(BookSourceKind.Folder, result.Book.SourceKind);
        Assert.AreEqual(Now, result.Book.AddedAt);
        Assert.IsNull(result.Book.LastReadAt);
    }

    /// <summary>
    /// 追加した本を識別子で取得すると、追加したときと同じ内容の本が返ることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task GetAsync_AddedBook_ReturnsSameBook()
    {
        using var repository = await OpenRepositoryAsync();
        var added = await repository.AddAsync("題名", "作者", FolderLocation, BookSourceKind.Folder, CancellationToken);

        var actual = await repository.GetAsync(added.Book.Id, CancellationToken);

        Assert.AreEqual(added.Book, actual);
    }

    /// <summary>
    /// 登録されていない識別子を指定すると、本が見つからなかったことが返ることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var repository = await OpenRepositoryAsync();
        await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);

        var actual = await repository.GetAsync(Guid.NewGuid(), CancellationToken);

        Assert.IsNull(actual);
    }

    /// <summary>
    /// 追加したすべての本が、一覧に含まれることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task ListAsync_AddedBooks_ContainsAllBooks()
    {
        using var repository = await OpenRepositoryAsync();
        var folder = await repository.AddAsync("フォルダの本", "作者", FolderLocation, BookSourceKind.Folder, CancellationToken);
        var zip = await repository.AddAsync("ZIP の本", null, ZipLocation, BookSourceKind.Zip, CancellationToken);

        var actual = await repository.ListAsync(CancellationToken);

        CollectionAssert.AreEquivalent(new[] { folder.Book, zip.Book }, actual.ToArray());
    }

    /// <summary>
    /// 保管庫を閉じて開き直しても、追加した本を、すべての項目が同じ内容で読み出せることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task OpenAsync_Reopened_ReadsSameBooks()
    {
        Book read;
        Book unread;
        using (var repository = await OpenRepositoryAsync())
        {
            var added = await repository.AddAsync("読んだ本", "作者", FolderLocation, BookSourceKind.Folder, CancellationToken);
            read = added.Book with { LastReadAt = Now.AddDays(1) };
            await repository.UpdateAsync(read, CancellationToken);

            unread = (await repository.AddAsync("読んでいない本", null, AndroidLocation, BookSourceKind.Pdf, CancellationToken)).Book;
        }

        using (var repository = await OpenRepositoryAsync())
        {
            Assert.AreEqual(read, await repository.GetAsync(read.Id, CancellationToken));
            Assert.AreEqual(unread, await repository.GetAsync(unread.Id, CancellationToken));

            var list = await repository.ListAsync(CancellationToken);
            CollectionAssert.AreEquivalent(new[] { read, unread }, list.ToArray());
        }
    }

    /// <summary>
    /// 作者と最後に読んだ日時が空の本を保存でき、開き直した後も、どちらも空のまま読み出せることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task AddAsync_WithoutAuthorAndLastReadAt_IsStoredAsEmpty()
    {
        Guid id;
        using (var repository = await OpenRepositoryAsync())
        {
            var added = await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);
            id = added.Book.Id;
        }

        using (var repository = await OpenRepositoryAsync())
        {
            var actual = await repository.GetAsync(id, CancellationToken);

            Assert.IsNotNull(actual);
            Assert.IsNull(actual.Author);
            Assert.IsNull(actual.LastReadAt);
        }
    }

    /// <summary>
    /// 題名、作者、最後に読んだ日時を更新でき、更新後の内容で読み出せることを確かめる。更新していない本は変わらないことも確かめる。
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_TitleAuthorAndLastReadAt_ReadsUpdatedBook()
    {
        using var repository = await OpenRepositoryAsync();
        var added = await repository.AddAsync("古い題名", "古い作者", FolderLocation, BookSourceKind.Folder, CancellationToken);
        var other = await repository.AddAsync("ほかの本", "ほかの作者", ZipLocation, BookSourceKind.Zip, CancellationToken);
        var updated = added.Book with
        {
            Title = "新しい題名",
            Author = "新しい作者",
            LastReadAt = Now.AddHours(3),
        };

        var isUpdated = await repository.UpdateAsync(updated, CancellationToken);

        Assert.IsTrue(isUpdated);
        Assert.AreEqual(updated, await repository.GetAsync(added.Book.Id, CancellationToken));
        Assert.AreEqual(other.Book, await repository.GetAsync(other.Book.Id, CancellationToken));
    }

    /// <summary>
    /// 設定済みの作者と最後に読んだ日時を、空に戻す更新ができることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_ClearAuthorAndLastReadAt_ReadsAsEmpty()
    {
        using var repository = await OpenRepositoryAsync();
        var added = await repository.AddAsync("題名", "作者", FolderLocation, BookSourceKind.Folder, CancellationToken);
        await repository.UpdateAsync(added.Book with { LastReadAt = Now.AddHours(3) }, CancellationToken);

        await repository.UpdateAsync(added.Book with { Author = null, LastReadAt = null }, CancellationToken);

        var actual = await repository.GetAsync(added.Book.Id, CancellationToken);
        Assert.IsNotNull(actual);
        Assert.IsNull(actual.Author);
        Assert.IsNull(actual.LastReadAt);
    }

    /// <summary>
    /// 更新する本の、元になるファイルの場所と種類、追加した日時を変えて渡しても、それらは保存済みの値のまま変わらないことを確かめる。
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_SourceAndAddedAtChanged_KeepsStoredValues()
    {
        using var repository = await OpenRepositoryAsync();
        var added = await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);

        await repository.UpdateAsync(
            added.Book with
            {
                SourceLocation = ZipLocation,
                SourceKind = BookSourceKind.Zip,
                AddedAt = Now.AddDays(-1),
            },
            CancellationToken);

        Assert.AreEqual(added.Book, await repository.GetAsync(added.Book.Id, CancellationToken));
    }

    /// <summary>
    /// 登録されていない本を更新しようとすると、更新できなかったことが返り、本が作られないことを確かめる。
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        using var repository = await OpenRepositoryAsync();
        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = "題名",
            SourceLocation = FolderLocation,
            SourceKind = BookSourceKind.Folder,
            AddedAt = Now,
        };

        var isUpdated = await repository.UpdateAsync(book, CancellationToken);

        Assert.IsFalse(isUpdated);
        Assert.IsEmpty(await repository.ListAsync(CancellationToken));
    }

    /// <summary>
    /// 削除した本は、識別子を指定しても取得できず、一覧にも含まれないことを確かめる。削除していない本は残ることも確かめる。
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_AddedBook_CannotBeGotOrListed()
    {
        using var repository = await OpenRepositoryAsync();
        var deleted = await repository.AddAsync("削除する本", null, FolderLocation, BookSourceKind.Folder, CancellationToken);
        var kept = await repository.AddAsync("残す本", null, ZipLocation, BookSourceKind.Zip, CancellationToken);

        var isDeleted = await repository.DeleteAsync(deleted.Book.Id, CancellationToken);

        Assert.IsTrue(isDeleted);
        Assert.IsNull(await repository.GetAsync(deleted.Book.Id, CancellationToken));
        var list = await repository.ListAsync(CancellationToken);
        Assert.AreEqual(kept.Book, Assert.ContainsSingle(list));
    }

    /// <summary>
    /// 登録されていない本を削除しようとすると、削除できなかったことが返り、ほかの本は残ることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        using var repository = await OpenRepositoryAsync();
        var kept = await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);

        var isDeleted = await repository.DeleteAsync(Guid.NewGuid(), CancellationToken);

        Assert.IsFalse(isDeleted);
        Assert.AreEqual(kept.Book, Assert.ContainsSingle(await repository.ListAsync(CancellationToken)));
    }

    /// <summary>
    /// すでに登録されている場所の本を追加しようとすると、新しい本は作られず、登録済みであることと、登録済みの本が返ることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task AddAsync_RegisteredLocation_ReturnsExistingBookWithoutAdding()
    {
        using var repository = await OpenRepositoryAsync();
        var first = await repository.AddAsync("最初の題名", "作者", FolderLocation, BookSourceKind.Folder, CancellationToken);

        var second = await repository.AddAsync("別の題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);

        Assert.IsTrue(second.IsAlreadyRegistered);
        Assert.AreEqual(first.Book, second.Book);
        Assert.AreEqual(first.Book, Assert.ContainsSingle(await repository.ListAsync(CancellationToken)));
    }

    /// <summary>
    /// 場所の重複は文字列の完全一致で判定し、大文字と小文字だけが違う場所は、別の本として登録されることを確かめる。
    /// 大文字と小文字の違いを吸収するのは、場所を受け取る側の役目にしている。
    /// </summary>
    [TestMethod]
    public async Task AddAsync_LocationDiffersOnlyInCase_AddsSeparateBook()
    {
        using var repository = await OpenRepositoryAsync();
        await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);

        var result = await repository.AddAsync("題名", null, FolderLocation.ToUpperInvariant(), BookSourceKind.Folder, CancellationToken);

        Assert.IsFalse(result.IsAlreadyRegistered);
        Assert.HasCount(2, await repository.ListAsync(CancellationToken));
    }

    /// <summary>
    /// 日時が、UTC の ISO 8601 形式の文字列（YYYY-MM-DDTHH:MM:SS.SSSZ）で保存されることを確かめる。
    /// UTC 以外のオフセットで渡した日時も、UTC に換算して保存されることを確かめる。
    /// 端末のタイムゾーンが UTC 以外の環境で実行すれば、端末のローカル時刻で保存してしまう誤りも検出できる。
    /// </summary>
    [TestMethod]
    public async Task Dates_AreStoredAsUtcIso8601Text()
    {
        Guid id;
        using (var repository = await OpenRepositoryAsync())
        {
            var added = await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);
            var lastReadAt = new DateTimeOffset(2026, 11, 12, 23, 45, 30, 765, TimeSpan.FromHours(9));
            await repository.UpdateAsync(added.Book with { LastReadAt = lastReadAt }, CancellationToken);
            id = added.Book.Id;
        }

        var (addedAt, lastReadAtText) = ReadStoredDates(_databasePath, id);

        Assert.AreEqual("2026-11-12T00:30:15.123Z", addedAt);
        Assert.AreEqual("2026-11-12T14:45:30.765Z", lastReadAtText);
    }

    /// <summary>
    /// 日時のうち、ミリ秒より細かい部分は切り捨てて保存することを確かめる。
    /// 追加した結果として返る本も、保存した内容と同じ値を持つことを確かめる。
    /// </summary>
    [TestMethod]
    public async Task Dates_SubMillisecond_AreTruncated()
    {
        var now = Now.AddTicks(9_999);
        var lastReadAt = Now.AddHours(1).AddTicks(9_999);
        using var repository = await BookRepository.OpenAsync(_databasePath, new FakeTimeProvider(now), CancellationToken);

        var added = await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);
        await repository.UpdateAsync(added.Book with { LastReadAt = lastReadAt }, CancellationToken);
        var actual = await repository.GetAsync(added.Book.Id, CancellationToken);

        Assert.AreEqual(Now, added.Book.AddedAt);
        Assert.IsNotNull(actual);
        Assert.AreEqual(Now, actual.AddedAt);
        Assert.AreEqual(Now.AddHours(1), actual.LastReadAt);
    }

    /// <summary>
    /// このアプリより新しい版の定義になったデータベースは開かず、扱えないことが呼び出し側で区別できることを確かめる。
    /// 新しい版で書かれたデータを、古い版のアプリが壊さないようにするため。
    /// </summary>
    [TestMethod]
    public async Task OpenAsync_NewerSchemaVersion_ThrowsNotSupportedException()
    {
        using (await OpenRepositoryAsync())
        {
        }

        SetSchemaVersionToNewer(_databasePath);

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => OpenRepositoryAsync());
    }

    /// <summary>
    /// 題名が空の本は追加できないことを確かめる。
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task AddAsync_EmptyTitle_ThrowsArgumentException(string title)
    {
        using var repository = await OpenRepositoryAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => repository.AddAsync(title, null, FolderLocation, BookSourceKind.Folder, CancellationToken));
    }

    /// <summary>
    /// 作者に空の文字列は渡せないことを確かめる。作者がいないことは、null だけで表す。
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task AddAsync_EmptyAuthor_ThrowsArgumentException(string author)
    {
        using var repository = await OpenRepositoryAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => repository.AddAsync("題名", author, FolderLocation, BookSourceKind.Folder, CancellationToken));
    }

    /// <summary>
    /// 元になるファイルの場所が空の本は追加できないことを確かめる。
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task AddAsync_EmptySourceLocation_ThrowsArgumentException(string sourceLocation)
    {
        using var repository = await OpenRepositoryAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => repository.AddAsync("題名", null, sourceLocation, BookSourceKind.Folder, CancellationToken));
    }

    /// <summary>
    /// 定義されていない種類の本は追加できないことを確かめる。
    /// </summary>
    [TestMethod]
    public async Task AddAsync_UndefinedSourceKind_ThrowsArgumentOutOfRangeException()
    {
        using var repository = await OpenRepositoryAsync();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => repository.AddAsync("題名", null, FolderLocation, (BookSourceKind)99, CancellationToken));
    }

    /// <summary>
    /// 題名を空にする更新と、作者を空の文字列にする更新はできず、保存済みの内容が変わらないことを確かめる。
    /// </summary>
    [TestMethod]
    [DataRow("", "作者")]
    [DataRow(" ", "作者")]
    [DataRow("題名", "")]
    [DataRow("題名", " ")]
    public async Task UpdateAsync_EmptyTitleOrAuthor_ThrowsArgumentException(string title, string author)
    {
        using var repository = await OpenRepositoryAsync();
        var added = await repository.AddAsync("元の題名", "元の作者", FolderLocation, BookSourceKind.Folder, CancellationToken);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => repository.UpdateAsync(added.Book with { Title = title, Author = author }, CancellationToken));

        Assert.AreEqual(added.Book, await repository.GetAsync(added.Book.Id, CancellationToken));
    }

    /// <summary>
    /// 閉じた保管庫は使えず、閉じた後に使おうとしたことが呼び出し側で区別できることを確かめる。
    /// </summary>
    [TestMethod]
    public async Task GetAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var repository = await OpenRepositoryAsync();
        var added = await repository.AddAsync("題名", null, FolderLocation, BookSourceKind.Folder, CancellationToken);

        repository.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => repository.GetAsync(added.Book.Id, CancellationToken));
    }

    private Task<BookRepository> OpenRepositoryAsync()
    {
        return BookRepository.OpenAsync(_databasePath, new FakeTimeProvider(Now), CancellationToken);
    }

    private static void SetSchemaVersionToNewer(string databasePath)
    {
        using var connection = OpenDatabaseDirectly(databasePath);

        // 定義の版を上げても新しい版のままでいられるように、十分に大きな値にする。
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version = 9999;";
        command.ExecuteNonQuery();
    }

    private static (string AddedAt, string? LastReadAt) ReadStoredDates(string databasePath, Guid id)
    {
        using var connection = OpenDatabaseDirectly(databasePath);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT added_at, last_read_at FROM books WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    // 保管庫を通さずに、データベースのファイルを開く。後片付けでファイルを消せるように、接続は使い回さない。
    private static SqliteConnection OpenDatabaseDirectly(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
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
