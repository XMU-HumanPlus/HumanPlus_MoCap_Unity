using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HumanPlusMoCap.Scripts
{
    /// <summary>
    /// UI 管理器：统一处理 UI 按钮事件与状态同步
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private MoCapManager moCapManager;

        [Header("Events")]
        [SerializeField] private UnityEvent calibrationCompleted;

        [Header("UI")]
        [SerializeField] private Text statusText;

        private Coroutine recalibrateRoutine;
        private Coroutine calibrationSuccessRoutine;

        private void OnEnable()
        {
            if (moCapManager != null)
            {
                moCapManager.CalibrationCompleted += HandleCalibrationCompleted;
            }
        }

        private void OnDisable()
        {
            if (moCapManager != null)
            {
                moCapManager.CalibrationCompleted -= HandleCalibrationCompleted;
            }
        }

        /// <summary>
        /// UI 调用：向 Python 端发送重新校准指令
        /// </summary>
        public void SendRecalibrateCommand()
        {
            if (moCapManager == null)
            {
                Debug.LogError("[UIManager] MoCapManager 未设置");
                return;
            }

            if (recalibrateRoutine != null)
            {
                StopCoroutine(recalibrateRoutine);
            }

            if (calibrationSuccessRoutine != null)
            {
                StopCoroutine(calibrationSuccessRoutine);
                calibrationSuccessRoutine = null;
            }

            recalibrateRoutine = StartCoroutine(RecalibrateCountdown());
        }

        /// <summary>
        /// UI 调用：退出系统
        /// </summary>
        public void ExitApplication()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleCalibrationCompleted()
        {
            if (recalibrateRoutine != null)
            {
                StopCoroutine(recalibrateRoutine);
                recalibrateRoutine = null;
            }

            if (calibrationSuccessRoutine != null)
            {
                StopCoroutine(calibrationSuccessRoutine);
            }

            calibrationSuccessRoutine = StartCoroutine(ShowCalibrationSuccess());
            calibrationCompleted?.Invoke();
        }

        private IEnumerator RecalibrateCountdown()
        {
            SetStatusText("请保持T-Pose（3）");
            yield return new WaitForSeconds(1f);

            SetStatusText("请保持T-Pose（2）");
            yield return new WaitForSeconds(1f);

            SetStatusText("请保持T-Pose（1）");
            moCapManager.SendRecalibrateCommand();
            yield return new WaitForSeconds(1f);

            SetStatusText("请等待");
            recalibrateRoutine = null;
        }

        private IEnumerator ShowCalibrationSuccess()
        {
            SetStatusText("校准成功");
            yield return new WaitForSeconds(1f);
            ClearStatusText();
            calibrationSuccessRoutine = null;
        }

        private void SetStatusText(string message)
        {
            if (statusText == null)
            {
                Debug.LogWarning("[UIManager] 状态文本未设置");
                return;
            }

            statusText.text = message;
            statusText.enabled = true;
        }

        private void ClearStatusText()
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = string.Empty;
            statusText.enabled = false;
        }
    }
}
