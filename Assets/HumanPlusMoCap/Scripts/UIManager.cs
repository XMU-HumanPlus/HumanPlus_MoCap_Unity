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
        [SerializeField] private GameObject characterRoot;
        [SerializeField] private BoneLengthAdjuster boneLengthAdjuster;

        [Header("Events")]
        [SerializeField] private UnityEvent calibrationCompleted;

        [Header("UI")]
        [SerializeField] private Text statusText;

        private Coroutine recalibrateRoutine;
        private Coroutine calibrationSuccessRoutine;

        /// <summary>
        /// 订阅校准完成事件。
        /// </summary>
        private void OnEnable()
        {
            if (moCapManager != null)
            {
                moCapManager.CalibrationCompleted += HandleCalibrationCompleted;
            }
        }

        /// <summary>
        /// 取消订阅校准完成事件。
        /// </summary>
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

        /// <summary>
        /// UI 调用：打开骨骼长度调整面板。
        /// </summary>
        public void OpenBoneLengthAdjuster()
        {
            if (boneLengthAdjuster == null)
            {
                boneLengthAdjuster = FindObjectOfType<BoneLengthAdjuster>();
            }

            if (boneLengthAdjuster == null)
            {
                Canvas canvas = FindObjectOfType<Canvas>();
                GameObject host = new GameObject("BoneLengthAdjuster");
                if (canvas != null)
                {
                    host.transform.SetParent(canvas.transform, false);
                }

                boneLengthAdjuster = host.AddComponent<BoneLengthAdjuster>();
            }

            if (boneLengthAdjuster == null)
            {
                Debug.LogError("[UIManager] 无法创建 BoneLengthAdjuster");
                return;
            }

            if (!boneLengthAdjuster.gameObject.activeSelf)
            {
                boneLengthAdjuster.gameObject.SetActive(true);
            }

            if (!boneLengthAdjuster.enabled)
            {
                boneLengthAdjuster.enabled = true;
            }

            boneLengthAdjuster.Open(characterRoot);
        }

        /// <summary>
        /// 校准完成回调：停止倒计时并提示成功。
        /// </summary>
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

        /// <summary>
        /// 校准倒计时协程。
        /// </summary>
        private IEnumerator RecalibrateCountdown()
        {
            SetStatusText("请保持T-Pose（3）");
            yield return new WaitForSeconds(1f);
            
            SetStatusText("请保持T-Pose（2）");
            yield return new WaitForSeconds(1f);

            SetStatusText("请保持T-Pose（1）");
            yield return new WaitForSeconds(.5f);

            SetStatusText("校准中，请保持T-Pose");
            moCapManager.SendRecalibrateCommand();
            yield return new WaitForSeconds(.5f);

            SetStatusText("请等待");
            recalibrateRoutine = null;
        }

        /// <summary>
        /// 显示校准成功提示协程。
        /// </summary>
        private IEnumerator ShowCalibrationSuccess()
        {
            SetStatusText("校准成功");
            yield return new WaitForSeconds(1f);
            ClearStatusText();
            calibrationSuccessRoutine = null;
        }

        /// <summary>
        /// 更新状态文本内容。
        /// </summary>
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

        /// <summary>
        /// 清空并隐藏状态文本。
        /// </summary>
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
