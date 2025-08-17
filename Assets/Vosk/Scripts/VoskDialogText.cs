using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class VoskDialogText : MonoBehaviour 
{
    public VoskSpeechToText VoskSpeechToText;
    public Text DialogText;

	Regex hi_regex = new Regex(@"привет");
	Regex who_regex = new Regex(@"кто ты");
	Regex pass_regex = new Regex("(хорошо|давай)");
	Regex help_regex = new Regex("помоги");

	Regex goat_regex = new Regex(@"(козу|начнём с козы)");
	Regex wolf_regex = new Regex(@"(волк|волка)");
	Regex cabbage_regex = new Regex(@"(капуста|капусту|начнём с капусты)");

	Regex goat_back_regex = new Regex(@"(козу назад|вернём козу|верни козу)");
	Regex wolf_back_regex = new Regex(@"(волка назад|вернём волка|верни волка)");
	Regex cabbage_back_regex = new Regex(@"(капусту назад|вернём капусту|верни капусту)");

	Regex forward_regex = new Regex("переедем");
	Regex back_regex = new Regex("(назад|вернёмся назад)");

	// State
	bool goat_left;
	bool wolf_left;
	bool cabbage_left;
	bool man_left;

    void Awake()
    {
        VoskSpeechToText.OnTranscriptionResult += OnTranscriptionResult;
		ResetState();
    }

	void ResetState()
	{
		goat_left = true;
		wolf_left = true;
		cabbage_left = true;
		man_left = true;
	}

	void CheckState() {
		if (goat_left && wolf_left && !man_left) {
			AddFinalResponse("волк съел козу, начинай сначала");
			return;
		}
		if (goat_left && cabbage_left && !man_left) {
			AddFinalResponse("коза съела капусту, начинай сначала");
			return;
		}
		if (!goat_left && !wolf_left && man_left) {
			AddFinalResponse("волк съел козу, начинай сначала");
			return;
		}
		if (!goat_left && !cabbage_left && man_left) {
			AddFinalResponse("коза съела капусту, начинай сначала");
			return;
		}
		if (!goat_left && !wolf_left && !cabbage_left && !man_left) {
			AddFinalResponse("отлично получилось, давай ещё раз!");
			return;
		}

		AddResponse("так, и что дальше");
	}

	void Say(string response)
	{
		System.Diagnostics.Process.Start("/usr/bin/say", response); 
	}

	void AddFinalResponse(string response) {
		Say(response);
		DialogText.text = response + "\n";
		ResetState();
	}

	void AddResponse(string response) {
        Say(response);
		DialogText.text = response + "\n\n";

		DialogText.text += "крестьянин " + (man_left ? "слева" : "справа") + "\n";
		DialogText.text += "волк " + (wolf_left ? "слева" : "справа") + "\n";
		DialogText.text += "коза " + (goat_left ? "слева" : "справа") + "\n";
		DialogText.text += "капуста " + (cabbage_left ? "слева" : "справа") + "\n";

		DialogText.text += "\n";
	}

    private void OnTranscriptionResult(string obj)
    {
		// Save to file

        Debug.Log(obj);
        var result = new RecognitionResult(obj);
        foreach (RecognizedPhrase p in result.Phrases)
        {
			if (hi_regex.IsMatch(p.Text))
			{
				AddResponse("привет тебе");
				return;
			}
			if (who_regex.IsMatch(p.Text))
			{
				AddResponse("я робот учитель");
				return;
			}
			if (pass_regex.IsMatch(p.Text))
			{
                AddResponse("отлично");
				return;
			}
			if (help_regex.IsMatch(p.Text))
			{
				AddResponse("думай сам");
				return;
			}
			if (goat_back_regex.IsMatch(p.Text)) {
				if (goat_left == true) {
					AddResponse("коза ещё на левом берегу");
				} else if (man_left == true) {
					AddResponse("крестьянин ещё на левом берегу");
				} else {
					goat_left = true;
					man_left = true;
					CheckState();
				}
				return;
			}

			if (wolf_back_regex.IsMatch(p.Text)) {
				if (wolf_left == true) {
					AddResponse("волк ещё на левом берегу");
				} else if (man_left == true) {
					AddResponse("крестьянин ещё на левом берегу");
				} else {
					wolf_left = true;
					man_left = true;
					CheckState();
				}
				return;
			}

			if (cabbage_back_regex.IsMatch(p.Text)) {
				if (cabbage_left == true) {
					AddResponse("капуста ещё на левом берегу");
				} else if (man_left == true) {
					AddResponse("крестьянин ещё на левом берегу");
				} else {
					cabbage_left = true;
					man_left = true;
					CheckState();
				}
				return;
			}

			if (goat_regex.IsMatch(p.Text)) {
				if (goat_left == false) {
					AddResponse("коза уже на правом берегу");
				} else if (man_left == false) {
					AddResponse("крестьянин уже на правом берегу");
				} else {
					goat_left = false;
					man_left = false;
					CheckState();
				}
				return;
			}

			if (wolf_regex.IsMatch(p.Text)) {
				if (wolf_left == false) {
					AddResponse("волк уже на правом берегу");
				} else if (man_left == false) {
					AddResponse("крестьянин уже на правом берегу");
				} else {
					wolf_left = false;
					man_left = false;
					CheckState();
				}
				return;
			}

			if (cabbage_regex.IsMatch(p.Text)) {
				if (cabbage_left == false) {
					AddResponse("капуста уже на правом берегу");
				} else if (man_left == false) {
					AddResponse("крестьянин уже на правом берегу");
				} else {
					cabbage_left = false;
					man_left = false;
					CheckState();
				}
				return;
			}

			if (forward_regex.IsMatch(p.Text)) {
				if (man_left == false) {
					AddResponse("крестьянин уже на правом берегу");
				} else {
					man_left = false;
					CheckState();
				}
				return;
			}
		
			if (back_regex.IsMatch(p.Text)) {
				if (man_left == true) {
					AddResponse("крестьянин ещё на левом берегу");
				} else {
					man_left = true;
					CheckState();
				}
				return;
			}
        }
		if (result.Phrases.Length > 0 && result.Phrases[0].Text != "") {
			AddResponse("я тебя не понимаю");
		}
    }
}
