/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./wwwroot/**/*.html",
    "./wwwroot/**/*.js"
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        primary: '#3B82F6',
        success: '#10B981',
        danger: '#EF4444',
      },
      screens: {
        'docs': '1100px', // Custom breakpoint for docs page inline search
      }
    }
  },
  plugins: [],
}
