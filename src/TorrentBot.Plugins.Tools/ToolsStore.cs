using Microsoft.Data.Sqlite;

namespace TorrentBot.Plugins.Tools;

public sealed class ToolsStore
{
    private readonly string _connectionString;
    public ToolsStore(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? Path.Combine("data", "homelynx-tools.db") : path;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString(); Initialize(); MigrateNotes();
    }
    private void Initialize() { using var c = new SqliteConnection(_connectionString); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = @"PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS notes(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,body TEXT NOT NULL,created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS todos(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,body TEXT NOT NULL,done INTEGER NOT NULL DEFAULT 0,created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS reminders(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,body TEXT NOT NULL,due TEXT NOT NULL,done INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS pastes(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,body TEXT NOT NULL,created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS polls(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,question TEXT NOT NULL,options TEXT NOT NULL,closed INTEGER NOT NULL DEFAULT 0,created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS poll_votes(poll_id INTEGER NOT NULL,user_id TEXT NOT NULL,option_index INTEGER NOT NULL,created TEXT NOT NULL,PRIMARY KEY(poll_id,user_id));
CREATE TABLE IF NOT EXISTS webhooks(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,url TEXT NOT NULL,label TEXT NOT NULL,revoked INTEGER NOT NULL DEFAULT 0,created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS short_links(id INTEGER PRIMARY KEY AUTOINCREMENT,user_id TEXT NOT NULL,code TEXT NOT NULL UNIQUE,url TEXT NOT NULL,title TEXT NOT NULL DEFAULT '',tags TEXT NOT NULL DEFAULT '',created TEXT NOT NULL,expires TEXT NULL,max_visits INTEGER NULL,visits INTEGER NOT NULL DEFAULT 0,disabled INTEGER NOT NULL DEFAULT 0);"; cmd.ExecuteNonQuery(); }
    private void MigrateNotes() { using var c = Open(); using var x = c.CreateCommand(); x.CommandText = "ALTER TABLE notes ADD COLUMN tags TEXT NOT NULL DEFAULT ''"; try { x.ExecuteNonQuery(); } catch (SqliteException) { } }
    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
    private static void P(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
    public async Task<long> AddNote(string u, string b, string tags = "") { using var c=Open(); using var x=c.CreateCommand(); x.CommandText="INSERT INTO notes(user_id,body,tags,created) VALUES($u,$b,$t,$d);SELECT last_insert_rowid();"; P(x,"$u",u);P(x,"$b",b);P(x,"$t",tags);P(x,"$d",DateTimeOffset.UtcNow.ToString("O"));return(long)(await x.ExecuteScalarAsync()??0); }
    public async Task<long> AddTodo(string u, string b) => await Add("todos", u, b, "body");
    public async Task<long> AddPaste(string u, string b) => await Add("pastes", u, b, "body");
    private async Task<long> Add(string table, string u, string b, string field) { using var c = Open(); using var x = c.CreateCommand(); x.CommandText = $"INSERT INTO {table}(user_id,{field},created) VALUES($u,$b,$d); SELECT last_insert_rowid();"; P(x,"$u",u); P(x,"$b",b); P(x,"$d",DateTimeOffset.UtcNow.ToString("O")); return (long)(await x.ExecuteScalarAsync() ?? 0); }
    public async Task<object[]> ListNotes(string u, string q) { using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT id,body,tags,created FROM notes WHERE user_id=$u AND ($q='' OR body LIKE '%'||$q||'%' OR tags LIKE '%'||$q||'%') ORDER BY id DESC LIMIT 50";P(x,"$u",u);P(x,"$q",q);var a=new List<object>();using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())a.Add($"#{r.GetInt64(0)} {r.GetString(1)}{(string.IsNullOrWhiteSpace(r.GetString(2))?"":" ["+r.GetString(2)+"]")} ({r.GetString(3)})");return a.ToArray(); }
    public async Task<object?> GetNote(string u,long id){using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT body,tags,created FROM notes WHERE id=$i AND user_id=$u";P(x,"$i",id);P(x,"$u",u);using var r=await x.ExecuteReaderAsync();return await r.ReadAsync()?$"#{id} {r.GetString(0)}{(string.IsNullOrWhiteSpace(r.GetString(1))?"":" ["+r.GetString(1)+"]")} ({r.GetString(2)})":null;}
    public async Task<object[]> ListTodos(string u, bool done) => await List("todos", u, done ? "done = 1" : "done = 0", "body", true);
    public async Task<object[]> ListPastes(string u) => await List("pastes", u, "", "body");
    private async Task<object[]> List(string table, string u, string q, string field, bool whereClause = false) { using var c=Open(); using var x=c.CreateCommand(); x.CommandText = whereClause ? $"SELECT id,{field},created FROM {table} WHERE user_id=$u AND {q} ORDER BY id DESC LIMIT 50" : $"SELECT id,{field},created FROM {table} WHERE user_id=$u AND ($q='' OR {field} LIKE '%'||$q||'%') ORDER BY id DESC LIMIT 50"; P(x,"$u",u); P(x,"$q",q); var a=new List<object>(); using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) a.Add($"#{r.GetInt64(0)} {r.GetString(1)} ({r.GetString(2)})"); return a.ToArray(); }
    public async Task SetTodo(string u,long id,bool done) => await Simple("UPDATE todos SET done=$d WHERE id=$i AND user_id=$u",u,id,done?1:0);
    public async Task UpdateTodo(string u,long id,string body)=>await UpdateBody("todos",u,id,body);
    public async Task ClearTodos(string u)=>await SimpleUser("DELETE FROM todos WHERE user_id=$u AND done=1",u);
    public async Task UpdateNote(string u,long id,string body)=>await UpdateBody("notes",u,id,body);
    public async Task SetNoteTags(string u,long id,string tags){using var c=Open();using var x=c.CreateCommand();x.CommandText="UPDATE notes SET tags=$t WHERE id=$i AND user_id=$u";P(x,"$t",tags);P(x,"$i",id);P(x,"$u",u);await x.ExecuteNonQueryAsync();}
    public async Task DeleteTodo(string u,long id)=>await Simple("DELETE FROM todos WHERE id=$i AND user_id=$u",u,id);
    public async Task DeleteNote(string u,long id)=>await Simple("DELETE FROM notes WHERE id=$i AND user_id=$u",u,id);
    public async Task DeletePaste(string u,long id)=>await Simple("DELETE FROM pastes WHERE id=$i AND user_id=$u",u,id);
    private async Task Simple(string sql,string u,long id,int? d=null){using var c=Open();using var x=c.CreateCommand();x.CommandText=sql;P(x,"$u",u);P(x,"$i",id);if(d.HasValue)P(x,"$d",d.Value);await x.ExecuteNonQueryAsync();}
    private async Task SimpleUser(string sql,string u){using var c=Open();using var x=c.CreateCommand();x.CommandText=sql;P(x,"$u",u);await x.ExecuteNonQueryAsync();}
    private async Task UpdateBody(string table,string u,long id,string body){using var c=Open();using var x=c.CreateCommand();x.CommandText=$"UPDATE {table} SET body=$b WHERE id=$i AND user_id=$u";P(x,"$b",body);P(x,"$i",id);P(x,"$u",u);await x.ExecuteNonQueryAsync();}
    public async Task<long> AddReminder(string u,string b,DateTimeOffset due)=>await AddReminderInternal(u,b,due);
    private async Task<long> AddReminderInternal(string u,string b,DateTimeOffset d){using var c=Open();using var x=c.CreateCommand();x.CommandText="INSERT INTO reminders(user_id,body,due) VALUES($u,$b,$d);SELECT last_insert_rowid();";P(x,"$u",u);P(x,"$b",b);P(x,"$d",d.ToString("O"));return(long)(await x.ExecuteScalarAsync()??0);}
    public async Task<object[]> ListReminders(string u)=>await ListScheduled(u,false);
    public async Task<object[]> ListTimers(string u)=>await ListScheduled(u,true);
    private async Task<object[]> ListScheduled(string u,bool timers){using var c=Open();using var x=c.CreateCommand();x.CommandText=timers?"SELECT id,body,due FROM reminders WHERE user_id=$u AND done=0 AND body LIKE 'TIMER:%' ORDER BY due":"SELECT id,body,due FROM reminders WHERE user_id=$u AND done=0 AND body NOT LIKE 'TIMER:%' ORDER BY due";P(x,"$u",u);var a=new List<object>();using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())a.Add($"#{r.GetInt64(0)} {r.GetString(1).Replace("TIMER: ","")} (due {r.GetString(2)})");return a.ToArray();}
    public async Task DeleteReminder(string u,long id)=>await Simple("DELETE FROM reminders WHERE id=$i AND user_id=$u",u,id);
    public async Task<long> AddPoll(string u,string q,string[] o)=>await AddPollInternal(u,q,string.Join(" | ",o));
    private async Task<long> AddPollInternal(string u,string q,string o){using var c=Open();using var x=c.CreateCommand();x.CommandText="INSERT INTO polls(user_id,question,options,created) VALUES($u,$q,$o,$d);SELECT last_insert_rowid();";P(x,"$u",u);P(x,"$q",q);P(x,"$o",o);P(x,"$d",DateTimeOffset.UtcNow.ToString("O"));return(long)(await x.ExecuteScalarAsync()??0);}
    public async Task<object[]> ListPolls(string u)=>await List("polls",u,"", "question");
    public async Task ClosePoll(string u,long id)=>await Simple("UPDATE polls SET closed=1 WHERE id=$i AND user_id=$u",u,id);
    public async Task VotePoll(string u,long id,int option){using var c=Open();using var x=c.CreateCommand();x.CommandText="INSERT INTO poll_votes(poll_id,user_id,option_index,created) VALUES($p,$u,$o,$d) ON CONFLICT(poll_id,user_id) DO UPDATE SET option_index=$o,created=$d";P(x,"$p",id);P(x,"$u",u);P(x,"$o",option);P(x,"$d",DateTimeOffset.UtcNow.ToString("O"));await x.ExecuteNonQueryAsync();}
    public async Task<object[]> PollResults(long id){using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT option_index,COUNT(*) FROM poll_votes WHERE poll_id=$p GROUP BY option_index ORDER BY option_index";P(x,"$p",id);var a=new List<object>();using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())a.Add($"Option {r.GetInt32(0)+1}: {r.GetInt32(1)} vote(s)");return a.ToArray();}
    public async Task<object?> GetPaste(string u,long id){using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT body FROM pastes WHERE id=$i AND user_id=$u";P(x,"$i",id);P(x,"$u",u);return await x.ExecuteScalarAsync();}
    public async Task<long> AddWebhook(string u,string url,string label)=>await AddWebhookInternal(u,url,label);
    private async Task<long>AddWebhookInternal(string u,string url,string label){using var c=Open();using var x=c.CreateCommand();x.CommandText="INSERT INTO webhooks(user_id,url,label,created) VALUES($u,$url,$l,$d);SELECT last_insert_rowid();";P(x,"$u",u);P(x,"$url",url);P(x,"$l",label);P(x,"$d",DateTimeOffset.UtcNow.ToString("O"));return(long)(await x.ExecuteScalarAsync()??0);}
    public async Task<object[]> ListWebhooks(string u)=>await List("webhooks",u,"", "label");
    public async Task<(string Url,string Label)?> GetWebhook(string u,long id){using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT url,label FROM webhooks WHERE id=$i AND user_id=$u AND revoked=0";P(x,"$i",id);P(x,"$u",u);using var r=await x.ExecuteReaderAsync();return await r.ReadAsync()?(r.GetString(0),r.GetString(1)):null;}
    public async Task RevokeWebhook(string u,long id)=>await Simple("UPDATE webhooks SET revoked=1 WHERE id=$i AND user_id=$u",u,id);

    public async Task CreateShortLink(string user, string code, string url, string title, string tags, DateTimeOffset? expires, int? maxVisits)
    {
        using var c=Open();using var x=c.CreateCommand();x.CommandText="INSERT INTO short_links(user_id,code,url,title,tags,created,expires,max_visits) VALUES($u,$c,$url,$t,$tags,$d,$e,$m)";P(x,"$u",user);P(x,"$c",code);P(x,"$url",url);P(x,"$t",title);P(x,"$tags",tags);P(x,"$d",DateTimeOffset.UtcNow.ToString("O"));P(x,"$e",expires?.ToString("O")??(object)DBNull.Value);P(x,"$m",maxVisits??(object)DBNull.Value);await x.ExecuteNonQueryAsync();
    }

    public async Task<ShortLinkRecord?> ResolveShortLink(string code, bool countVisit)
    {
        using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT id,user_id,code,url,title,tags,created,expires,max_visits,visits,disabled FROM short_links WHERE code=$c";P(x,"$c",code);using var r=await x.ExecuteReaderAsync();if(!await r.ReadAsync())return null;
        var record=new ShortLinkRecord(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),DateTimeOffset.Parse(r.GetString(6)),r.IsDBNull(7)?null:DateTimeOffset.Parse(r.GetString(7)),r.IsDBNull(8)?null:r.GetInt32(8),r.GetInt32(9),r.GetInt32(10)!=0);
        var accepted=!record.Disabled&&(record.Expires is null||record.Expires>DateTimeOffset.UtcNow)&&(record.MaxVisits is null||record.Visits<record.MaxVisits);
        if(countVisit)
        {
            using var update=c.CreateCommand();
            update.CommandText="UPDATE short_links SET visits=visits+1 WHERE id=$i AND disabled=0 AND (expires IS NULL OR expires>$now) AND (max_visits IS NULL OR visits<max_visits)";
            P(update,"$i",record.Id);P(update,"$now",DateTimeOffset.UtcNow.ToString("O"));
            accepted=await update.ExecuteNonQueryAsync()>0;
            if(accepted) record=record with { Visits=record.Visits+1 };
        }
        record=record with { VisitAccepted=accepted };
        return record;
    }

    public async Task<object[]> ListShortLinks(string user){using var c=Open();using var x=c.CreateCommand();x.CommandText="SELECT code,url,title,expires,max_visits,visits,disabled FROM short_links WHERE user_id=$u ORDER BY id DESC LIMIT 100";P(x,"$u",user);var a=new List<object>();using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())a.Add($"{r.GetString(0)} -> {r.GetString(1)}{(string.IsNullOrWhiteSpace(r.GetString(2))?"":" ["+r.GetString(2)+"]")} visits={r.GetInt32(5)}{(r.GetInt32(6)!=0?" DISABLED":"")}");return a.ToArray();}
    public async Task DisableShortLink(string user,string code)=>await SimpleShortLink("UPDATE short_links SET disabled=1 WHERE user_id=$u AND code=$c",user,code);
    public async Task DeleteShortLink(string user,string code)=>await SimpleShortLink("DELETE FROM short_links WHERE user_id=$u AND code=$c",user,code);
    private async Task SimpleShortLink(string sql,string user,string code){using var c=Open();using var x=c.CreateCommand();x.CommandText=sql;P(x,"$u",user);P(x,"$c",code);await x.ExecuteNonQueryAsync();}
}

public sealed record ShortLinkRecord(long Id,string UserId,string Code,string Url,string Title,string Tags,DateTimeOffset Created,DateTimeOffset? Expires,int? MaxVisits,int Visits,bool Disabled,bool VisitAccepted=true);
