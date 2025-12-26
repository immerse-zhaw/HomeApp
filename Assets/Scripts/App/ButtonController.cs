using UnityEngine;

public class ButtonController : MonoBehaviour
{
    [Header("Buttons to Control")]
    [SerializeField] private System.Collections.Generic.List<UnityEngine.UI.Button> buttons;

    private int selectedIndex = 0;
    private UnityEngine.Color selectedColor = new UnityEngine.Color(0.9f, 0.9f, 0.9f, 1f); // Off-white
    private UnityEngine.Color unselectedColor = new UnityEngine.Color(0.1f, 0.1f, 0.1f, 1f); // #1A1A1A
    private UnityEngine.Color hoverColor = new UnityEngine.Color(0.25f, 0.25f, 0.25f, 1f); // Lighter grey for hover

    void Awake()
    {
        if (buttons == null || buttons.Count == 0)
            return;

        // Set up listeners
        foreach (var btn in buttons)
        {
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnButtonClicked(btn));
            }
        }

        // Highlight only Apps (first button) at start
        HighlightButton(0);
    }

    private void OnButtonClicked(UnityEngine.UI.Button clicked)
    {
        int idx = buttons.IndexOf(clicked);
        if (idx >= 0)
        {
            HighlightButton(idx);
        }
    }

    private void HighlightButton(int idx)
    {
        // Set all buttons to unselected color with hover effect
        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i] != null)
            {
                var colorBlock = buttons[i].colors;
                colorBlock.normalColor = unselectedColor;
                colorBlock.highlightedColor = hoverColor; // Allow hover effect
                colorBlock.pressedColor = unselectedColor;
                colorBlock.selectedColor = unselectedColor;
                buttons[i].colors = colorBlock;
            }
        }

        // Set the selected button to selected color (no hover change)
        if (buttons[idx] != null)
        {
            var colorBlock = buttons[idx].colors;
            colorBlock.normalColor = selectedColor;
            colorBlock.highlightedColor = selectedColor;
            colorBlock.pressedColor = selectedColor;
            colorBlock.selectedColor = selectedColor;
            buttons[idx].colors = colorBlock;
        }
        
        selectedIndex = idx;
    }

    // No need to handle deselection or outside press, Unity's Button does not change color unless pressed
}
