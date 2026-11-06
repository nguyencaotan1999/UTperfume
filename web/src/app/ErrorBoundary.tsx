import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  failed: boolean
}

/** Catches render errors so one broken page does not blank the whole app. Details stay in the console. */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false }

  static getDerivedStateFromError(): State {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled UI error', error, info.componentStack)
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <div role="alert" className="notice notice--error">
        <h1>Something went wrong</h1>
        <p>The page could not be displayed. Try again, or go back to the home page.</p>
        <button type="button" onClick={() => this.setState({ failed: false })}>
          Try again
        </button>{' '}
        <a href="/">Home</a>
      </div>
    )
  }
}
