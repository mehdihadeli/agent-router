import DefaultTheme from "vitepress/theme";
import HomeSections from "./HomeSections.vue";
import "./custom.css";

export default {
	extends: DefaultTheme,
	enhanceApp({ app }) {
		app.component("HomeSections", HomeSections);
	},
};
