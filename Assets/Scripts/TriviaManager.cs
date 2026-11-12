using Fusion;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Singleton network behavoir that manages the game and is managed by the shared mode master client.
/// </summary>
public class TriviaManager : NetworkBehaviour, IStateAuthorityChanged
{
    [Tooltip("Scriptable object that contains the trivia question.")]
    public TriviaQuestionSet triviaQuestions;

    [Tooltip("Container for the trivial question elements")]
    public GameObject questionElements = null;

    #region Networked Properties
    [Networked, Tooltip("Timer used for asking questions and transitioning between different states.")]
    public TickTimer timer { get; set; }

    [Networked, Tooltip("The length of the timer, used to help get a percentage when rendering timers.")]
    public float timerLength { get; set; }

    [Tooltip("The index of the current question.  -1 means no question is being currently asked.")]
    [Networked, OnChangedRender(nameof(UpdateCurrentQuestion))]
    public int CurrentQuestion { get; set; } = -1;

    [Tooltip("The number of questions asked asked so far.")]
    [Networked, OnChangedRender(nameof(UpdateQuestionsAskedText))]
    public int QuestionsAsked { get; set; } = 0;

    [Tooltip("The current state of the game.")]
    [Networked, OnChangedRender(nameof(OnTriviaGameStateChanged))]
    public TriviaGameState GameState { get; set; } = TriviaGameState.Intro;


    /// <summary>
    /// A randomized array of each question index.
    /// </summary>
    [Networked, Capacity(50)]
    public NetworkArray<int> randomizedQuestionList => default;

    #endregion

    #region UI ELEMENETS

    /// <summary>
    /// Question, answer, and answer highlights
    /// </summary>
    public TextMeshProUGUI question;
    public TextMeshProUGUI[] answers;
    public Image[] answerHighlights;

    /// <summary>
    /// The visual used to display the game
    /// </summary>
    public Image timerVisual;

    [Tooltip("Gradient used to color the timer based on percentage.")]
    public Gradient timerVisualGradient;

    /// <summary>
    /// Displays which questions out of 10 we are on.
    /// </summary>
    public TextMeshProUGUI questionIndicatorText;

    /// <summary>
    /// Text message shown when the game changes state.
    /// </summary>
    public TextMeshProUGUI triviaMessage;

    [Tooltip("Button displayed to leave the game after a round ends.")]
    public GameObject leaveGameBtn;

    [Tooltip("Button displayed, only to the master client, to start a new game.")]
    public GameObject startNewGameBtn;

    [Tooltip("MonoBehaviour that displays winner at the end of a game.")]
    public TriviaEndGame endGameObject;

    #endregion

    [Header("Game Rules")]
    [Tooltip("The maximum number of questions to ask in a round.")]
    [Min(1)]
    public int maxQuestions = 2;

    [Tooltip("The amount of time the questions will be asked for.")]
    public float questionLength = 30;

    [Tooltip("The minimum number of points earned for getting a question correct")]
    public int pointsPerQuestions;

    [Tooltip("The amount of points earned based on the percentage of remaining time.  So 100 would make the player earn 50 points if they answered at the 50% mark.")]
    public int timeBonus;

    #region SFX
    [Header("SFX Audio Sources")]
    [SerializeField, Tooltip("AudioSource played when the local player gets an answer correct.")]
    private AudioSource _correctSFX;

    [SerializeField, Tooltip("AudioSource played when the local player gets an answer incorrect.")]
    private AudioSource _incorrectSFX;

    [SerializeField, Tooltip("AudioSource played when the local player selects an answer.")]
    private AudioSource _confirmSFX;

    [SerializeField, Tooltip("AudioSource played when the local player selects an answer.")]
    private AudioSource _errorSFX;
    #endregion

    /// <summary>
    /// Has a trivia manager been made; set to true on spawn and false on despawn
    /// </summary>
    public static bool TriviaManagerPresent { get; private set; } = false;

    /// <summary>
    /// The different states of the trivia game.  Made as a byte since there are not that many.
    /// </summary>
    public enum TriviaGameState : byte
    {
        Intro = 0,
        ShowQuestion = 1,
        ShowAnswer = 2,
        GameOver = 3,
        NewRound = 4,
    }

    public override void Spawned()
    {

        // Disallows players from joining once the game is started.
        if (Runner.IsSharedModeMasterClient)
        {
            Runner.SessionInfo.IsOpen = false;
            Runner.SessionInfo.IsVisible = false;
        }

        // If we have state authority, we set an intro time and randomized the question list.
        if (HasStateAuthority)
        {
            // Sets an initial intro timer
            // The initial timer for the game is only 3 seconds.
            timerLength = 3f;
            timer = TickTimer.CreateFromSeconds(Runner, timerLength);

            ShuffleQuestions();
        }

        TriviaManagerPresent = true;

        FusionConnector.Instance?.SetPregameKey(null);
        OnTriviaGameStateChanged();
        UpdateCurrentQuestion();
        UpdateQuestionsAskedText();
    }

    /// <summary>
    /// Shuffles the questions around.
    /// </summary>
    private void ShuffleQuestions()
    {
        Debug.Log("SHUFFLINF QUESTIONG!");

        // Creates a temp list, adding every index avaiable
        List<int> questionsAvailable = new List<int>();
        for (int i = 0; i < triviaQuestions.Questions.Count; i++)
            questionsAvailable.Add(i);

        // Sets the fifty questions
        for (int i = 0; i < randomizedQuestionList.Length; i++)
        {
            int c = questionsAvailable[Random.Range(0, questionsAvailable.Count)];
            questionsAvailable.Remove(c);
            randomizedQuestionList.Set(i, c);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        TriviaManagerPresent = false;
    }

    /// <summary>
    /// Note, was a bit confused because only the shared mode master client seems to run this.
    /// Game worked fine, but unsure if every client should be running this in their simulation or not.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // When the timer expires...
        if (timer.Expired(Runner))
        {
            // If we are showing a question, we then show an answer...
            if (GameState == TriviaGameState.ShowQuestion)
            {
                timerLength = 3f;
                timer = TickTimer.CreateFromSeconds(Runner, timerLength);
                GameState = TriviaGameState.ShowAnswer;
                return;
            }
            else if (QuestionsAsked < maxQuestions)
            {
                TriviaPlayer.LocalPlayer.ChosenAnswer = -1;

                // This means we are at the end of the question list and want to reshuffle the answers
                if (CurrentQuestion + 1 >= randomizedQuestionList.Length)
                {
                    ShuffleQuestions();
                    CurrentQuestion = 0;
                }
                else
                {
                    CurrentQuestion++;
                }
                
                QuestionsAsked++;

                timerLength = questionLength;
                timer = TickTimer.CreateFromSeconds(Runner, timerLength);
                GameState = TriviaGameState.ShowQuestion;
            }
            else
            {
                timer = TickTimer.None;
                GameState = TriviaGameState.GameOver;
            }

            return;
        }

        // We check to see if every player has chosen answer, and if so, go to the show answer state.
        if (GameState == TriviaGameState.ShowQuestion)
        {
            int totalAnswers = 0;
            for (int i = 0; i < TriviaPlayer.TriviaPlayerRefs.Count; i++)
            {
                if (TriviaPlayer.TriviaPlayerRefs[i].ChosenAnswer >= 0)
                {
                    totalAnswers++;
                }
            }
            if (totalAnswers == TriviaPlayer.TriviaPlayerRefs.Count)
            {
                timerLength = 3f;
                timer = TickTimer.CreateFromSeconds(Runner, timerLength);
                GameState = TriviaGameState.ShowAnswer;
            }
        }
    }
    private string _winnerName = null;

    private void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    private void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    private void OnLocaleChanged(Locale _)
    {
        UpdateQuestionsAskedText();
        ApplyQuestionTexts();
        UpdateTriviaMessage();
    }

    private void UpdateTriviaMessage()
    {
        switch (GameState)
        {
            case TriviaGameState.Intro:
                triviaMessage.text = Loc.Get("UI", "msg_intro"); break;
            case TriviaGameState.NewRound:
                triviaMessage.text = Loc.Get("UI", "msg_newround"); break;
            case TriviaGameState.GameOver:
                triviaMessage.text = string.IsNullOrEmpty(_winnerName)
                    ? Loc.Get("UI", "msg_no_winners")
                    : Loc.Get("UI", "msg_winner", _winnerName);
                break;
            default:
                triviaMessage.text = string.Empty; break;
        }
    }

    private void ApplyQuestionTexts()
    {
        if (CurrentQuestion < 0) return;

        var q = triviaQuestions.Questions[randomizedQuestionList[CurrentQuestion]];
        question.text = q.GetQuestion();
        answers[0].text = q.GetCorrect();
        answers[1].text = q.GetDecoy(0);
        answers[2].text = q.GetDecoy(1);
        answers[3].text = q.GetDecoy(2);
    }

    /// <summary>
    /// Called when a player picks an answer.
    /// </summary>
    /// <param name="index"></param>
    public void PickAnswer(int index)
    {
        // If we are in the question state and the local player has not picked an answer...
        if (GameState == TriviaGameState.ShowQuestion)
        {
            // For now, if Chosen Answer is less than 0, this means they haven't picked an answer.
            // We don't allow players to pick new answers at this time.
            if (TriviaPlayer.LocalPlayer.ChosenAnswer < 0)
            {
                _confirmSFX.Play();

                TriviaPlayer.LocalPlayer.ChosenAnswer = index;

                // Colors the highlighted question cyan.
                answerHighlights[index].color = Color.cyan;

                float? remainingTime = timer.RemainingTime(Runner);
                if (remainingTime.HasValue)
                {
                    float percentage = remainingTime.Value / this.timerLength;
                    TriviaPlayer.LocalPlayer.TimerBonusScore = Mathf.RoundToInt(timeBonus * percentage);
                }
                else
                {
                    TriviaPlayer.LocalPlayer.TimerBonusScore = 0;
                }
            }
            else
            {
                _errorSFX.Play();
            }
        }
    }

    /// <summary>
    /// Update function that updates the timer visual.
    /// </summary>
    public void Update()
    {
        // Updates the timer visual
        float? remainingTime = timer.RemainingTime(Runner);
        if (remainingTime.HasValue)
        {
            float percent = remainingTime.Value / timerLength;
            timerVisual.fillAmount = percent;
            timerVisual.color = timerVisualGradient.Evaluate(percent);
        }
        else
        {
            timerVisual.fillAmount = 0f;
        }
    }

    private void OnTriviaGameStateChanged()
    {
        // If showin an answer, we show which players got the question correct and increase their score.
        if (GameState == TriviaGameState.Intro || GameState == TriviaGameState.NewRound)
        {
            // Resets the score for the player
            TriviaPlayer.LocalPlayer.Score = 0;

            TriviaPlayer.LocalPlayer.Expression = TriviaPlayer.AvatarExpressions.Neutral;

            UpdateTriviaMessage();
            endGameObject.Hide();
        }
        else if (GameState == TriviaGameState.ShowAnswer)
        {
            OnGameStateShowAnswer();

            endGameObject.Hide();
        }
        else if (GameState == TriviaGameState.GameOver)
        {
            OnGameStateGameOver();
        }
        else if (GameState == TriviaGameState.ShowQuestion)
        {
            // Otherwise, we clear the color of the answers
            for (int i = 0; i < answers.Length; i++)
            {
                answerHighlights[i].color = Color.clear;
            }

            triviaMessage.text = string.Empty;

            endGameObject.Hide();
        }

        leaveGameBtn.SetActive(GameState == TriviaGameState.GameOver);
        startNewGameBtn.SetActive(GameState == TriviaGameState.GameOver && Runner.IsSharedModeMasterClient == true);
    }

    private void OnGameStateGameOver()
    {
        // Hides the question elements and then shows the game elements / final score / winner elements
        questionElements.SetActive(false);

        // Removes the correct answer highlight
        for (int i = 0; i < answers.Length; i++)
        {
            answerHighlights[i].color = Color.clear;
        }

        // OrderByDescending es estable: mismo orden en todos los clientes aunque haya empates
        var ranking = TriviaPlayer.TriviaPlayerRefs.OrderByDescending(p => p.Score).ToList();
        List<TriviaPlayer> winners = ranking.Where(p => p.Score > 0).Take(3).ToList();

        endGameObject.Show(winners);

        _winnerName = winners.Count == 0 ? null : winners[0].PlayerName.Value;
        UpdateTriviaMessage();

        // Puntuación mínima para estar en el podio (los empates con el 3.º también cuentan)
        int minPodiumScore = winners.Count == 3 ? winners[2].Score : 1;
        bool localOnPodium = TriviaPlayer.LocalPlayer.Score > 0 &&
                             TriviaPlayer.LocalPlayer.Score >= minPodiumScore;

        TriviaPlayer.LocalPlayer.Expression = localOnPodium
            ? TriviaPlayer.AvatarExpressions.Happy_CorrectAnswer
            : TriviaPlayer.AvatarExpressions.Angry_WrongAnswer;
    }

    private void OnGameStateShowAnswer()
    {
        triviaMessage.text = string.Empty;

        // If the player picks the correct answer, their score increases
        if (TriviaPlayer.LocalPlayer.ChosenAnswer == 0)
        {
            TriviaPlayer.LocalPlayer.Expression = TriviaPlayer.AvatarExpressions.Happy_CorrectAnswer;

            int scoreValue = pointsPerQuestions + TriviaPlayer.LocalPlayer.TimerBonusScore;

            // Gets the score pop up and toggles it.
            var scorePopUp = TriviaPlayer.LocalPlayer.ScorePopUp;
            scorePopUp.Score = scoreValue;
            scorePopUp.Toggle = !scorePopUp.Toggle;
            TriviaPlayer.LocalPlayer.ScorePopUp = scorePopUp;

            TriviaPlayer.LocalPlayer.Score += scoreValue;

            _correctSFX.Play();
        }
        else
        {
            // Gets score value and toggles it
            var scorePopUp = TriviaPlayer.LocalPlayer.ScorePopUp;
            scorePopUp.Score = 0;
            scorePopUp.Toggle = !scorePopUp.Toggle;
            TriviaPlayer.LocalPlayer.ScorePopUp = scorePopUp;

            TriviaPlayer.LocalPlayer.Expression = TriviaPlayer.AvatarExpressions.Angry_WrongAnswer;

            _incorrectSFX.Play();
        }

        // Turns the answer the player chose to red to show they got it incorrect.
        if (TriviaPlayer.LocalPlayer.ChosenAnswer > 0)
        {
            answerHighlights[TriviaPlayer.LocalPlayer.ChosenAnswer].color = Color.red;
        }

        answerHighlights[0].color = Color.green;
    }

    private void UpdateCurrentQuestion()
    {
        // If we are asking a question, we set the answers.
        if (CurrentQuestion >= 0)
        {
            questionElements.SetActive(true);

            int questionIndex = randomizedQuestionList[CurrentQuestion];

            ApplyQuestionTexts();

            // Scrambles the answers.  This can be different per player.
            List<int> answerIndices = new List<int>() { 0, 1, 2, 3 };
            while (answerIndices.Count > 0)
            {
                int r = Random.Range(0, answerIndices.Count);
                answers[answerIndices[r]].transform.parent.SetSiblingIndex(0);
                answerIndices.RemoveAt(r);
            }

            // Clears the trivia message
            triviaMessage.text = string.Empty;

            // Deisgnate that the local player has not chosen an answer yet.
            TriviaPlayer.LocalPlayer.ChosenAnswer = -1;

            // Change the game state
            if (HasStateAuthority)
            {
                GameState = TriviaGameState.ShowQuestion;
            }
        }

        // We hide the question element in case a player late joins at the end of the game.
        if (GameState != TriviaGameState.ShowAnswer && GameState != TriviaGameState.ShowQuestion)
        {
            questionElements.SetActive(false);
        }
    }

    private void UpdateQuestionsAskedText()
    {
        questionIndicatorText.text = QuestionsAsked == 0
            ? ""
            : Loc.Get("UI", "question_counter", QuestionsAsked, maxQuestions);
    }

    public async void LeaveGame()
    {
        await Runner.Shutdown(true, ShutdownReason.Ok);

        FusionConnector fc = GameObject.FindObjectOfType<FusionConnector>();
        if (fc)
        {
            fc.mainMenuObject.SetActive(true);
            fc.mainGameObject.SetActive(false);
        }
    }

    public void StartNewGame()
    {
        if (HasStateAuthority == false)
            return;

        GameState = TriviaGameState.NewRound;

        QuestionsAsked = 0;

        // Sets an initial intro timer
        timerLength = 3f;
        timer = TickTimer.CreateFromSeconds(Runner, timerLength);
    }

    public void StateAuthorityChanged()
    {
        if (GameState == TriviaGameState.GameOver)
        {
            startNewGameBtn.SetActive(Runner.IsSharedModeMasterClient);
        }
    }
}
