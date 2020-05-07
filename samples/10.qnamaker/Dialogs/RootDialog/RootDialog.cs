﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.Xml;
using Microsoft.Bot.Builder.AI.QnA.Recognizers;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Adaptive;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Actions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Generators;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Input;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Templates;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.BotBuilderSamples
{
    public class RootDialog : ComponentDialog
    {
        private Templates _lgFile;

        public RootDialog(IConfiguration configuration)
            : base(nameof(RootDialog))
        {
            _lgFile = Templates.ParseFile(Path.Combine(".", "Dialogs", "RootDialog", "RootDialog.lg"));
            var rootDialog = new AdaptiveDialog(nameof(AdaptiveDialog))
            {
                Generator = new TemplateEngineLanguageGenerator(_lgFile),

                Recognizer = GetQnARecognizer(configuration),

                // Recognizer = MultiRecognizer(),
                Triggers = new List<OnCondition>()
                {
                    new OnConversationUpdateActivity()
                    {
                        Actions = WelcomeUserAction()
                    },
                    
                    // With QnA Maker set as a recognizer on a dialog, you can use the OnQnAMatch triger to render the answer.
                    new OnQnAMatch()
                    {
                        Actions = new List<Dialog>()
                        {
                            new SendActivity()
                            {
                                Activity = new ActivityTemplate("Here's what I have from QnA Maker - ${@answer}"),
                            }
                        }
                    },

                    // Should your QnA Maker KB support multi-turn, you can use an additional OnQnAMatch trigger to continue to rely on QnA Maker to drive the multi-turn conversation.
                    new OnQnAMatch()
                    {
                        Condition = "count(turn.recognized.answers[0].context.prompts) > 0",
                        Actions = new List<Dialog>()
                        {
                            new SetProperties()
                            {
                                Assignments = new List<PropertyAssignment>()
                                {
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.qnaContext",
                                        Value = "=turn.recognized.answers[0].context.prompts"
                                    },
                                    
                                    //new PropertyAssignment()
                                    //{
                                    //    Property = "dialog.qnaContext.PreviousQnAId",
                                    //    Value = "=turn.recognized.answers[0].id"
                                    //},
                                    //new PropertyAssignment()
                                    //{
                                    //    Property = "dialog.qnaContext.previousUserQuery",
                                    //    Value = "=turn.recognized.text"
                                    //},
                                    //new PropertyAssignment()
                                    //{
                                    //    Property = "dialog.qna.multiTurn.context",
                                    //    Value = "=turn.recognized.answers[0].context.prompts"
                                    //}
                                }
                            },
                            new TextInput()
                            {
                                Prompt = new ActivityTemplate("${ShowMultiTurnAnswer()}"),
                                Property = "turn.qnaMultiTurnResponse",
                                AllowInterruptions = "false",
                            },
                            new SetProperty()
                            {
                                Property = "turn.qnaMatchFromContext",
                                Value = "=where(dialog.qnaContext, item, item.displayText == turn.qnaMultiTurnResponse)"
                            },
                            new IfCondition()
                            {
                                Condition = "turn.qnaMatchFromContext && count(turn.qnaMatchFromContext) > 0",
                                Actions = new List<Dialog>()
                                {
                                    new SetProperty()
                                    {
                                        Property = "turn.qnaIdFromPrompt",
                                        Value = "=turn.qnaMatchFromContext[0].qnaId"
                                    },
                                    new EmitEvent()
                                    {
                                        EventName = DialogEvents.ActivityReceived,
                                        EventValue = "=turn.activity"
                                    }
                                },
                                ElseActions = new List<Dialog>()
                                {
                                    new DeleteProperty()
                                    {
                                        Property = "dialog.qnaContext"
                                    }
                                }
                            }
                        }
                    },
                    new OnUnknownIntent()
                    {
                        Actions = new List<Dialog>()
                        {
                            new SendActivity("${UnknownReadBack()}")
                        }
                    }
                }
            };

            // Add named dialogs to the DialogSet. These names are saved in the dialog state.
            AddDialog(rootDialog);

            // The initial child Dialog to run.
            InitialDialogId = nameof(AdaptiveDialog);
        }

        private static List<Dialog> WelcomeUserAction()
        {
            return new List<Dialog>()
            {
                // Iterate through membersAdded list and greet user added to the conversation.
                new Foreach()
                {
                    ItemsProperty = "turn.activity.membersAdded",
                    Actions = new List<Dialog>()
                    {
                        // Note: Some channels send two conversation update events - one for the Bot added to the conversation and another for user.
                        // Filter cases where the bot itself is the recipient of the message. 
                        new IfCondition()
                        {
                            Condition = "dialog.foreach.value.name != turn.activity.recipient.name",
                            Actions = new List<Dialog>()
                            {
                                new SendActivity("${WelcomeUser()}")
                            }
                        }
                    }
                }
            };
        }

        private static Recognizer GetQnARecognizer(IConfiguration configuration)
        {
            if (string.IsNullOrEmpty(configuration["qna:qnamakerSampleBot_en_us_qna"]) || string.IsNullOrEmpty(configuration["qna:hostname"]) || string.IsNullOrEmpty(configuration["qna:endpointKey"]))
            {
                throw new Exception("NOTE: QnA Maker is not configured for RootDialog. Please follow instructions in README.md. To enable all capabilities, add 'qnamaker:qnamakerSampleBot_en_us_qna', 'qnamaker:LuisAPIKey' and 'qnamaker:endpointKey' to the appsettings.json file.");
            }

            var recognizer = new QnAMakerRecognizer()
            {
                HostName = configuration["qna:hostname"],
                EndpointKey = configuration["qna:endpointKey"],
                KnowledgeBaseId = configuration["qna:qnamakerSampleBot_en_us_qna"],
               
                // property path that holds qna context
                Context = "dialog.qnaContext",

                // Property path where previous qna id is set. This is required to have multi-turn QnA working.
                QnAId = "turn.qnaIdFromPrompt",
                LogPersonalInformation = false,
                IncludeDialogNameInMetadata = false
            };

            return recognizer;
        }
    }
}
