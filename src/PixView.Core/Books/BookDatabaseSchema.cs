using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PixView.Core.Books;

/// <summary>
/// 本の情報を保存するデータベースの定義。
/// </summary>
/// <remarks>
/// 定義の版番号は、SQLite の <c>user_version</c> に記録する。
/// 定義を変えるときは、版番号を 1 つ上げ、前の版から移行する処理を <see cref="Initialize"/> に加える。
/// </remarks>
internal static class BookDatabaseSchema
{
    /// <summary>
    /// このアプリが扱える、定義の版番号。
    /// </summary>
    public const int CurrentVersion = 1;

    // SQLite は、CREATE TABLE の文をコメントも含めてそのまま保存するので、列の説明はここに書く。
    private const string CreateVersion1 = """
        CREATE TABLE books (
            -- 本の識別子（GUID）。
            id TEXT NOT NULL PRIMARY KEY,
            title TEXT NOT NULL,
            -- 作者。設定していなければ NULL。
            author TEXT,
            -- 元になるファイルの場所。同じ場所の本は 1 冊だけ登録できる。重複は、文字列の完全一致で判定する。
            source_location TEXT NOT NULL UNIQUE,
            -- 元になるファイルの種類。BookSourceKind の値。
            source_kind INTEGER NOT NULL,
            -- 日時は、UTC の ISO 8601 形式の文字列（YYYY-MM-DDTHH:MM:SS.SSSZ）で記録する。
            -- 桁数が決まっているので、文字列の順序が時刻の順序と一致する。
            added_at TEXT NOT NULL,
            -- 最後に読んだ日時。まだ読んでいなければ NULL。
            last_read_at TEXT
        ) STRICT;
        PRAGMA user_version = 1;
        """;

    /// <summary>
    /// データベースを、このアプリが扱える版の定義にする。
    /// </summary>
    /// <param name="connection">開いた接続。</param>
    /// <exception cref="NotSupportedException">
    /// データベースが、このアプリより新しい版の定義になっている。
    /// </exception>
    public static void Initialize(SqliteConnection connection)
    {
        // 書き込みのロックを最初に取り、同じファイルを同時に初期化しても、定義を二重に作らないようにする。
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            var version = GetVersion(connection);
            if (version > CurrentVersion)
            {
                throw new NotSupportedException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"保存先のデータベースは、このアプリより新しい版の定義（版 {version}）になっています。このアプリが扱える版は {CurrentVersion} までです。"));
            }

            if (version < 1)
            {
                using var command = connection.CreateCommand();
                command.CommandText = CreateVersion1;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        // 読み出しと書き込みを並行できるように、WAL にする。設定はデータベースのファイルに残る。
        // 扱えない新しい版のデータベースの設定を変えないように、版を確かめた後に行う。
        // トランザクションの中では切り替えられないので、トランザクションを終えてから行う。
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }
    }

    private static long GetVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }
}
