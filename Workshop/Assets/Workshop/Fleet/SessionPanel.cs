using UnityEngine;
using UnityEngine.UI;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// One floating panel: a translucent card with a title line and a body
    /// of text. Stage 1 shows static text; stage 2 binds it to a live fleet
    /// session. The panel is a world-space Canvas on a grabbable body, so
    /// hands can take it and put it anywhere in the room.
    /// </summary>
    public sealed class SessionPanel : MonoBehaviour
    {
        public Text title;
        public Text body;
        public Text status;

        public void Set(string titleText, string statusText, string bodyText)
        {
            if (title) title.text = titleText;
            if (status) status.text = statusText;
            if (body) body.text = bodyText;
        }

        public void Append(string fragment)
        {
            if (!body) return;
            body.text += fragment;
        }
    }
}
