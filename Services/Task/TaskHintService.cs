using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>
    /// 線索提示邏輯：v5 每個任務只有一段提示（task.task_hint），
    /// 玩家答錯至少一次（wrongCount &gt;= 1）才給提示，HintStage 固定為 1。
    /// </summary>
    public class TaskHintService
    {
        private const int HintTriggerWrongCount = 1;

        private readonly TaskHintDao _dao;

        public TaskHintService(TaskHintDao dao)
        {
            _dao = dao;
        }

        public async Task<HintResponse> GetHintAsync(string taskId, int wrongCount)
        {
            if (!int.TryParse(taskId, out int id) || wrongCount < HintTriggerWrongCount)
            {
                return new HintResponse { Available = false, HintStage = 0, HintText = null, LlmPromptTemplate = null };
            }

            string hint = await _dao.GetTaskHintAsync(id);
            if (hint == null)
            {
                return new HintResponse { Available = false, HintStage = 0, HintText = null, LlmPromptTemplate = null };
            }

            return new HintResponse
            {
                Available = true,
                HintStage = 1,
                HintText = hint,
                LlmPromptTemplate = null
            };
        }
    }
}