using UnityEngine;
using UnityEngine.Rendering;

namespace Mush.Lobby
{
    /// <summary>
    /// Procedural facial reactions for the unrigged low-poly lobby dogs.
    /// The imported eye and mouth meshes remain unchanged; this component only
    /// animates their transforms and creates a short-lived heart display.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushLobbyDogExpression : MonoBehaviour
    {
        [SerializeField] private MushLobbyDogRoamer roamer;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;
        [SerializeField] private Transform mouth;
        [SerializeField] private Camera viewerCamera;

        private Vector3 leftEyeRestScale;
        private Vector3 rightEyeRestScale;
        private Vector3 mouthRestScale;
        private Vector3 mouthRestPosition;
        private float petTimer;
        private float loveTimer;
        private GameObject tongue;
        private TextMesh[] hearts;
        private TextMesh petHint;

        public void Configure(
            MushLobbyDogRoamer newRoamer,
            Transform newHead,
            Transform newLeftEye,
            Transform newRightEye,
            Transform newMouth,
            Camera newViewerCamera)
        {
            roamer = newRoamer;
            head = newHead;
            leftEye = newLeftEye;
            rightEye = newRightEye;
            mouth = newMouth;
            viewerCamera = newViewerCamera;
            CaptureRestPose();
            EnsurePetHint();
        }

        private void Awake()
        {
            if (roamer == null)
                roamer = GetComponent<MushLobbyDogRoamer>();
            if (viewerCamera == null)
                viewerCamera = Camera.main;

            CaptureRestPose();
            EnsureTongue();
            EnsureHearts();
            EnsurePetHint();
        }

        private void Update()
        {
            bool loving = loveTimer > 0f;
            bool enjoying = loving || petTimer > 0f;
            float eyeOpen = enjoying ? (loving ? 0.22f : 0.38f) : 1f;
            float smileWidth = loving ? 1.65f : enjoying ? 1.18f : 1f;
            float blend = 1f - Mathf.Exp(-13f * Time.deltaTime);

            AnimateEye(leftEye, leftEyeRestScale, eyeOpen, blend);
            AnimateEye(rightEye, rightEyeRestScale, eyeOpen, blend);

            if (mouth != null)
            {
                Vector3 targetScale = mouthRestScale;
                targetScale.x *= smileWidth;
                targetScale.y *= loving ? 0.78f : 1f;
                mouth.localScale = Vector3.Lerp(mouth.localScale, targetScale, blend);

                Vector3 targetPosition = mouthRestPosition;
                if (loving)
                    targetPosition.y -= 0.018f;
                mouth.localPosition = Vector3.Lerp(mouth.localPosition, targetPosition, blend);
            }

            UpdateTongue(loving);
            UpdateHearts();
            UpdatePetHint();

            petTimer = Mathf.Max(0f, petTimer - Time.deltaTime);
            loveTimer = Mathf.Max(0f, loveTimer - Time.deltaTime);
        }

        public void ShowPetEnjoyment()
        {
            if (petHint != null)
                petHint.gameObject.SetActive(false);
            petTimer = Mathf.Max(petTimer, 1.35f);
            roamer?.WagTail(1.55f);
        }

        public void ShowLoveCelebration()
        {
            if (petHint != null)
                petHint.gameObject.SetActive(false);
            petTimer = Mathf.Max(petTimer, 2.8f);
            loveTimer = 2.8f;
            roamer?.WagTail(2.8f);
            EnsureHearts();
        }

        private void CaptureRestPose()
        {
            if (leftEye != null)
                leftEyeRestScale = leftEye.localScale;
            if (rightEye != null)
                rightEyeRestScale = rightEye.localScale;
            if (mouth != null)
            {
                mouthRestScale = mouth.localScale;
                mouthRestPosition = mouth.localPosition;
            }
        }

        private static void AnimateEye(Transform eye, Vector3 restScale, float open, float blend)
        {
            if (eye == null)
                return;

            Vector3 target = restScale;
            target.y *= open;
            eye.localScale = Vector3.Lerp(eye.localScale, target, blend);
        }

        private void EnsureTongue()
        {
            if (tongue != null || mouth == null)
                return;

            tongue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tongue.name = "Happy Tongue";
            tongue.transform.SetParent(transform, true);
            tongue.transform.localScale = new Vector3(0.075f, 0.050f, 0.035f);

            Collider tongueCollider = tongue.GetComponent<Collider>();
            if (tongueCollider != null)
                Destroy(tongueCollider);

            Renderer renderer = tongue.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material material = new Material(shader) { name = "Runtime Happy Tongue" };
                Color tongueColor = new Color(0.92f, 0.20f, 0.30f);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tongueColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", tongueColor);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            tongue.SetActive(false);
        }

        private void UpdateTongue(bool visible)
        {
            EnsureTongue();
            if (tongue == null || mouth == null)
                return;

            tongue.SetActive(visible);
            if (!visible)
                return;

            tongue.transform.position = mouth.position + transform.forward * 0.025f - Vector3.up * 0.035f;
            tongue.transform.rotation = transform.rotation;
        }

        private void EnsureHearts()
        {
            if (hearts != null)
                return;

            hearts = new TextMesh[3];
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int index = 0; index < hearts.Length; index++)
            {
                GameObject heartObject = new GameObject("Love Heart " + (index + 1));
                heartObject.transform.SetParent(transform, true);
                TextMesh heart = heartObject.AddComponent<TextMesh>();
                heart.text = "♥";
                heart.anchor = TextAnchor.MiddleCenter;
                heart.alignment = TextAlignment.Center;
                heart.fontSize = 72;
                heart.characterSize = 0.105f;
                heart.color = index == 1
                    ? new Color(1f, 0.24f, 0.42f, 1f)
                    : new Color(1f, 0.08f, 0.20f, 1f);
                if (font != null)
                {
                    heart.font = font;
                    MeshRenderer meshRenderer = heartObject.GetComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = font.material;
                    meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                }
                heartObject.SetActive(false);
                hearts[index] = heart;
            }
        }

        private void EnsurePetHint()
        {
            // VR에서는 손을 실제로 머리 쪽에 가져가 쓰다듬는 행동 자체가 안내가 되므로,
            // 개 머리 위에 떠 있던 "쓰다듬기" 글자는 더 이상 생성하지 않는다.
            if (petHint != null)
            {
                Destroy(petHint.gameObject); // 이전 플레이 상태나 핫 리로드로 남아 있는 안내가 있으면 즉시 제거한다.
                petHint = null; // 제거된 TextMesh 참조를 비워 이후 업데이트가 다시 사용하지 않게 한다.
            }
        }

        private void UpdatePetHint()
        {
            EnsurePetHint(); // 안내 생성은 하지 않고 혹시 남아 있는 구형 안내만 정리한다.
        }

        private void UpdateHearts()
        {
            EnsureHearts();
            if (hearts == null || head == null)
                return;

            float elapsed = 2.8f - loveTimer;
            for (int index = 0; index < hearts.Length; index++)
            {
                TextMesh heart = hearts[index];
                if (heart == null)
                    continue;

                float localTime = elapsed - index * 0.22f;
                bool active = loveTimer > 0f && localTime >= 0f && localTime <= 1.8f;
                heart.gameObject.SetActive(active);
                if (!active)
                    continue;

                float normalized = Mathf.Clamp01(localTime / 1.8f);
                float side = index - 1f;
                heart.transform.position = head.position +
                                           transform.right * (side * 0.16f) +
                                           Vector3.up * (0.22f + normalized * 0.42f);
                float pulse = 0.85f + Mathf.Sin(localTime * 10f) * 0.12f;
                heart.transform.localScale = Vector3.one * pulse;

                Camera camera = viewerCamera != null ? viewerCamera : Camera.main;
                if (camera != null)
                    heart.transform.rotation = Quaternion.LookRotation(heart.transform.position - camera.transform.position);

                Color color = heart.color;
                color.a = 1f - Mathf.InverseLerp(1.25f, 1.8f, localTime);
                heart.color = color;
            }
        }
    }
}
