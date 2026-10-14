import {
  importProvidersFrom,
  provideZonelessChangeDetection,
} from "@angular/core";
import { bootstrapApplication } from "@angular/platform-browser";
import { LucideAngularModule } from "lucide-angular";
import { AppComponent } from "./app/app.component";
import { UI_ICONS } from "./app/shared/ui/icons";

bootstrapApplication(AppComponent, {
  providers: [
    provideZonelessChangeDetection(),
    importProvidersFrom(LucideAngularModule.pick(UI_ICONS)),
  ],
}).catch(() => {
  document.body.textContent =
    "AgentRouter could not start. Reload to try again.";
});
