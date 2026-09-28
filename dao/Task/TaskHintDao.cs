using System.Threading.Tasks;
using Dapper;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    /// <summary>任務線索提示（task.task_hint）資料存取物件。</summary>
    public class TaskHintDao
    {
        private readonly AppSettings _appSettings;

        public TaskHintDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        /// <summary>取得任務提示（task.task_hint），沒有提示時回傳 null。</summary>
        public async Task<string> GetTaskHintAsync(int taskId)
        {
            using var conn = new MySqlConnection(_appSettings.mydb);
            string hint = await conn.ExecuteScalarAsync<string>(
                "SELECT task_hint FROM task WHERE task_id = @taskId;", new { taskId });
            return string.IsNullOrWhiteSpace(hint) ? null : hint;
        }
    }
}
