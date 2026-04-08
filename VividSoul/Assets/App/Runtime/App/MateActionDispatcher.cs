#nullable enable

using System;
using VividSoul.Runtime.AI;

namespace VividSoul.Runtime.App
{
    public sealed class MateActionDispatcher
    {
        private readonly DesktopPetRuntimeController runtimeController;

        public MateActionDispatcher(DesktopPetRuntimeController runtimeController)
        {
            this.runtimeController = runtimeController ?? throw new ArgumentNullException(nameof(runtimeController));
        }

        public void Dispatch(ConversationActionRequest? request)
        {
            if (request == null)
            {
                return;
            }

            switch (request.Kind)
            {
                case ConversationActionKind.PlayBuiltInPose:
                    if (!string.IsNullOrWhiteSpace(request.ActionId))
                    {
                        runtimeController.PlayConversationBuiltInPose(request.ActionId);
                    }

                    break;
            }
        }
    }
}
